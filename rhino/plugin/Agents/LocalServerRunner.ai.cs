using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

// Drives an OpenAI-compatible chat completions server (llama.cpp, LM Studio, vLLM, Ollama, ...; by
// default http://localhost:1234, no login, optional key) and acts as an MCP client for the document's
// Rhino MCP server, so the model can call Rhino's tools.
//
//   panel prompt -> POST {host}/v1/chat/completions (stream, with tools)
//                -> model asks for tool calls -> POST {mcpUrl} tools/call -> results fed back -> repeat
//
// Images returned by tools (screenshots) are saved to a temp PNG and the path is put in the tool result
// text, because the panel only renders an image when a result mentions a file path.
internal sealed class LocalServerRunner : IAgentRunner
{
    private const int MaxRounds = 15;
    private const int MaxToolDescription = 600;
    private const int MaxToolResultChars = 20_000;

    private static HttpClient Http { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    private sealed class ToolAcc
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder Args { get; } = new();
    }

    private AgentDefinition Definition { get; }
    private SemaphoreSlim TurnGate { get; } = new(1, 1);
    private object Gate { get; } = new();
    private CancellationTokenSource? Running { get; set; }
    private List<JsonObject> History { get; } = [];
    private string? ResolvedModel { get; set; }

    private string? ToolsUrl { get; set; }
    private JsonArray? Tools { get; set; }
    private int RpcId { get; set; }
    private TokenUsage TurnUsage { get; set; } = TokenUsage.Empty;

    public LocalServerRunner(AgentDefinition def, string docTitle)
        : this(def, new Conversation(Guid.NewGuid(), def.Name, docTitle))
    {
    }

    public LocalServerRunner(AgentDefinition def, Conversation conversation)
    {
        Definition = def;
        Conversation = conversation;

        // A restored transcript has no raw history, so rebuild the text of it from the shown turns.
        foreach (Turn turn in conversation.Turns)
        {
            History.Add(new JsonObject { ["role"] = "user", ["content"] = turn.Prompt });
            StringBuilder reply = new();
            foreach (TurnEvent ev in turn.Events)
                if (ev.Kind is TurnEventKind.AssistantText or TurnEventKind.Result)
                    reply.Append(ev.Text);
            if (reply.Length > 0)
                History.Add(new JsonObject { ["role"] = "assistant", ["content"] = reply.ToString() });
        }
    }

    public string Name => Definition.Name;

    public Conversation Conversation { get; }

    private string Host => AISettings.EffectiveHost(Definition).TrimEnd('/');

    // Adds "Authorization: Bearer <key>" when a key is set. Only for requests to the model server,
    // never for the Rhino MCP calls.
    private void Authorize(HttpRequestMessage request)
    {
        string key = AISettings.AgentApiKey(Definition.Name).Trim();
        if (key.Length > 0)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
    }

    public async Task PromptAsync(UserMessage message, string mcpUrl, string cwd)
    {
        await TurnGate.WaitAsync().ConfigureAwait(false);
        CancellationTokenSource cts = new();
        lock (Gate)
            Running = cts;

        try
        {
            Conversation.BeginTurn(message.Text, [.. message.Attachments.Select(static a => a.Info)]);
            TurnUsage = TokenUsage.Empty;
            History.Add(new JsonObject { ["role"] = "user", ["content"] = message.Text });

            await EnsureToolsAsync(mcpUrl, cts.Token).ConfigureAwait(false);
            string model = await ResolveModelAsync(cts.Token).ConfigureAwait(false);

            for (int round = 0; round < MaxRounds; round++)
            {
                (string text, List<ToolAcc> calls) = await StreamRoundAsync(model, round, cts.Token).ConfigureAwait(false);

                if (calls.Count == 0)
                {
                    if (text.Length > 0)
                        History.Add(new JsonObject { ["role"] = "assistant", ["content"] = text });
                    break;
                }

                JsonArray callArray = new();
                foreach (ToolAcc call in calls)
                {
                    callArray.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = NormalizeArgs(call.Args.ToString()) },
                    });
                }
                History.Add(new JsonObject { ["role"] = "assistant", ["content"] = text, ["tool_calls"] = callArray });

                foreach (ToolAcc call in calls)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    string args = NormalizeArgs(call.Args.ToString());
                    Conversation.Record(TurnEventKind.ToolUse, call.Name, args: args, id: call.Id);

                    (string forModel, string forPanel, bool failed) = await CallToolAsync(call.Name, args, cts.Token).ConfigureAwait(false);
                    Conversation.CompleteToolCall(call.Id, forPanel, failed);
                    History.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = forModel });
                }

                if (round == MaxRounds - 1)
                    Conversation.Record(TurnEventKind.AssistantText, "\n[stopped after too many tool rounds]");
            }
        }
        catch (OperationCanceledException)
        {
            Conversation.Record(TurnEventKind.AssistantText, "\n[stopped]");
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // Cancelling closes the response stream mid-read, which surfaces as ObjectDisposed/IO errors.
            Conversation.Record(TurnEventKind.AssistantText, "\n[stopped]");
        }
        catch (HttpRequestException ex) when (ex.InnerException is not null)
        {
            // Connection refused and the like: most likely no server is running at that address.
            Conversation.Record(TurnEventKind.AssistantText,
                $"Could not reach a model server at {Host} ({ex.InnerException.Message}). " +
                "Start an OpenAI-compatible server there (for example LM Studio or llama.cpp), or change the server URL in the AI settings.");
        }
        catch (Exception ex)
        {
            Conversation.Record(TurnEventKind.AssistantText, $"Could not get a reply from {Host}: {ex.Message}");
        }
        finally
        {
            lock (Gate)
                Running = null;
            cts.Dispose();
            Conversation.CompleteTurn();
            try { ConversationStore.Save(Conversation); } catch (Exception) { }
            TurnGate.Release();
        }
    }

    // ---- model side -------------------------------------------------------------------------------

    // The model set in the AI settings, or else whatever the server has loaded: the first one it lists.
    private async Task<string> ResolveModelAsync(CancellationToken ct)
    {
        string configured = AISettings.EffectiveModel(Definition);
        if (configured.Length > 0 && !configured.Equals("default", StringComparison.OrdinalIgnoreCase))
            return configured;
        if (ResolvedModel is not null)
            return ResolvedModel;

        List<string> modelIds = [];
        using HttpRequestMessage modelsRequest = new(HttpMethod.Get, $"{Host}/v1/models");
        Authorize(modelsRequest);
        using HttpResponseMessage response = await Http.SendAsync(modelsRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in data.EnumerateArray())
                if (item.TryGetProperty("id", out JsonElement id) && id.GetString() is { Length: > 0 } modelId)
                    modelIds.Add(modelId);
        if (modelIds.Count > 0)
            return ResolvedModel = modelIds[0];

        throw new InvalidOperationException("The server listed no models at /v1/models.");
    }

    private async Task<(string Text, List<ToolAcc> Calls)> StreamRoundAsync(string model, int round, CancellationToken ct)
    {
        JsonArray messages = new();
        string prompt = AISettings.EffectivePrompt(Definition);
        if (prompt.Length > 0)
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = prompt });
        foreach (JsonObject item in History)
            messages.Add(Clone(item));

        JsonObject payload = new()
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
        };
        if (Tools is { Count: > 0 })
            payload["tools"] = Clone(Tools);

        using HttpRequestMessage request = new(HttpMethod.Post, $"{Host}/v1/chat/completions")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        Authorize(request);
        using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new HttpRequestException($"{(int)response.StatusCode} {error}");
        }

        StringBuilder reply = new();
        StringBuilder thinking = new();
        SortedDictionary<int, ToolAcc> calls = new();
        bool thinkingOpen = false;
        string thinkId = $"think{round}";

        // Shows the model's reasoning as one expandable "Thinking" chip, kept out of the history.
        void CloseThinking()
        {
            if (!thinkingOpen) return;
            thinkingOpen = false;
            Conversation.CompleteToolCall(thinkId, thinking.ToString());
        }

        using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using StreamReader reader = new(stream);
        // net48 has no cancellable ReadLineAsync: cancelling closes the stream, which ends the read.
        using CancellationTokenRegistration cancelRead = ct.Register(stream.Dispose);
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string data = line.Substring(5).Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            JsonDocument chunk;
            try { chunk = JsonDocument.Parse(data); }
            catch (JsonException) { continue; }

            using (chunk)
            {
                JsonElement root = chunk.RootElement;

                if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement choice in choices.EnumerateArray())
                    {
                        if (!choice.TryGetProperty("delta", out JsonElement delta)) continue;

                        if ((delta.TryGetProperty("reasoning_content", out JsonElement reasoning) || delta.TryGetProperty("reasoning", out reasoning)) &&
                            reasoning.ValueKind == JsonValueKind.String &&
                            reasoning.GetString() is { Length: > 0 } thought)
                        {
                            if (!thinkingOpen)
                            {
                                thinkingOpen = true;
                                Conversation.Record(TurnEventKind.ToolUse, "Thinking…", id: thinkId);
                            }
                            thinking.Append(thought);
                        }

                        if (delta.TryGetProperty("content", out JsonElement content) &&
                            content.ValueKind == JsonValueKind.String &&
                            content.GetString() is { Length: > 0 } text)
                        {
                            CloseThinking();
                            reply.Append(text);
                            Conversation.Record(TurnEventKind.AssistantText, text);
                        }

                        if (delta.TryGetProperty("tool_calls", out JsonElement toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                        {
                            CloseThinking();
                            foreach (JsonElement tc in toolCalls.EnumerateArray())
                            {
                                int index = tc.TryGetProperty("index", out JsonElement ix) && ix.TryGetInt32(out int i) ? i : 0;
                                if (!calls.TryGetValue(index, out ToolAcc? acc))
                                    calls[index] = acc = new ToolAcc();

                                if (tc.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String &&
                                    idEl.GetString() is { Length: > 0 } id)
                                    acc.Id = id;

                                if (tc.TryGetProperty("function", out JsonElement fn))
                                {
                                    if (fn.TryGetProperty("name", out JsonElement nm) && nm.ValueKind == JsonValueKind.String)
                                        acc.Name += nm.GetString();
                                    if (fn.TryGetProperty("arguments", out JsonElement ar) && ar.ValueKind == JsonValueKind.String)
                                        acc.Args.Append(ar.GetString());
                                }
                            }
                        }
                    }
                }

                if (root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    int input = usage.TryGetProperty("prompt_tokens", out JsonElement p) && p.TryGetInt32(out int pi) ? pi : 0;
                    int output = usage.TryGetProperty("completion_tokens", out JsonElement c) && c.TryGetInt32(out int ci) ? ci : 0;
                    TurnUsage += new TokenUsage(input, output);
                    Conversation.RecordUsage(TurnUsage);
                }
            }
        }

        CloseThinking();

        List<ToolAcc> result = [.. calls.Values.Where(c => c.Name.Length > 0)];
        for (int n = 0; n < result.Count; n++)
            if (result[n].Id.Length == 0)
                result[n].Id = $"call_{round}_{n}";

        return (reply.ToString(), result);
    }

    // A node can only have one parent, so each request gets a copy (JsonNode.DeepClone is not on net48).
    private static JsonNode Clone(JsonNode node) => JsonNode.Parse(node.ToJsonString())!;

    // The model sometimes sends empty or broken argument text; the history must hold valid JSON.
    private static string NormalizeArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return "{}";
        try
        {
            JsonDocument.Parse(args).Dispose();
            return args;
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    // ---- MCP client (the document's own Rhino MCP server: stateless JSON-RPC over HTTP POST) ------------

    private async Task<JsonElement> RpcAsync(string url, string method, JsonObject? parameters, CancellationToken ct)
    {
        JsonObject body = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ++RpcId,
            ["method"] = method,
        };
        if (parameters is not null)
            body["params"] = parameters;

        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

        using HttpResponseMessage response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("error", out JsonElement error))
            throw new InvalidOperationException(error.TryGetProperty("message", out JsonElement m) ? m.GetString() : error.GetRawText());

        return doc.RootElement.TryGetProperty("result", out JsonElement result) ? result.Clone() : default;
    }

    private async Task EnsureToolsAsync(string mcpUrl, CancellationToken ct)
    {
        if (Tools is not null && ToolsUrl == mcpUrl) return;

        try
        {
            JsonElement result = await RpcAsync(mcpUrl, "tools/list", null, ct).ConfigureAwait(false);
            JsonArray tools = new();

            if (result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("tools", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tool in list.EnumerateArray())
                {
                    string? name = tool.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name == "ask_user") continue; // needs the panel's question UI, not wired to this runner

                    string description = tool.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? "" : "";
                    if (description.Length > MaxToolDescription)
                        description = description.Substring(0, MaxToolDescription) + "…";

                    JsonNode? schema = tool.TryGetProperty("inputSchema", out JsonElement s) ? JsonNode.Parse(s.GetRawText()) : null;
                    if (schema is not JsonObject schemaObject)
                        schemaObject = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
                    if (!schemaObject.ContainsKey("type"))
                        schemaObject["type"] = "object";
                    if (!schemaObject.ContainsKey("properties"))
                        schemaObject["properties"] = new JsonObject();

                    tools.Add(new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = schemaObject },
                    });
                }
            }

            Tools = tools;
            ToolsUrl = mcpUrl;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Tools = new JsonArray();
            ToolsUrl = null;
            Conversation.NoteSystem($"Rhino tools unavailable ({ex.Message}); chatting without them.");
        }
    }

    // Returns (text for the model, text for the panel, failed). The panel text carries image file paths.
    private async Task<(string ForModel, string ForPanel, bool Failed)> CallToolAsync(string name, string argsJson, CancellationToken ct)
    {
        try
        {
            JsonObject args = JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject();
            JsonElement result = await RpcAsync(ToolsUrl ?? "", "tools/call", new JsonObject { ["name"] = name, ["arguments"] = args }, ct).ConfigureAwait(false);

            bool failed = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("isError", out JsonElement e) && e.ValueKind == JsonValueKind.True;
            StringBuilder forModel = new();
            StringBuilder forPanel = new();

            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement block in content.EnumerateArray())
                {
                    string type = block.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "" : "";
                    if (type == "text" && block.TryGetProperty("text", out JsonElement txt))
                    {
                        string s = txt.GetString() ?? "";
                        forModel.AppendLine(s);
                        forPanel.AppendLine(s);
                    }
                    else if (type == "image" && block.TryGetProperty("data", out JsonElement data))
                    {
                        string mime = block.TryGetProperty("mimeType", out JsonElement mt) ? mt.GetString() ?? "image/png" : "image/png";
                        string path = SaveImage(data.GetString() ?? "", mime);
                        forPanel.AppendLine($"![image]({path})");
                        forModel.AppendLine("[An image was captured and is shown to the user; you cannot view it.]");
                    }
                }
            }

            if (forModel.Length == 0) forModel.Append(failed ? "The tool failed." : "Done.");

            string modelText = forModel.ToString();
            if (modelText.Length > MaxToolResultChars)
                modelText = modelText.Substring(0, MaxToolResultChars) + "\n[truncated]";

            return (modelText, forPanel.ToString(), failed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = $"Tool call failed: {ex.Message}";
            return (message, message, true);
        }
    }

    private static string SaveImage(string base64, string mime)
    {
        string extension = mime.IndexOf("jp", StringComparison.OrdinalIgnoreCase) >= 0 ? ".jpg" : ".png";
        string folder = Path.Combine(Path.GetTempPath(), "RhinoAI-localhost");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    // ---- IAgentRunner ------------------------------------------------------------------------------

    public Task LoginAsync(string mcpUrl, string cwd)
    {
        Conversation.NoteSystem($"No sign-in needed for {Host}.");
        return Task.CompletedTask;
    }

    public void Cancel()
    {
        lock (Gate)
            Running?.Cancel();
    }

    public void Dispose()
    {
        Cancel();
        // TurnGate is deliberately not disposed, see AgentRunner.Dispose.
    }
}
