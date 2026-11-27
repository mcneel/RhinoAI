using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed record CodexRateLimitWindow(int UsedPercent, long? WindowMinutes, DateTimeOffset? ResetsAt);

internal sealed record CodexRateLimits(CodexRateLimitWindow? Primary, CodexRateLimitWindow? Secondary, string? PlanType, string? LimitReached);

internal sealed class CodexHarness(PlugIns.PlugInToken token) : Harness(token)
{

    private static readonly string[] DisabledFeatures = ["shell_tool", "unified_exec", "multi_agent", "goals",
        "view_image", "sleep_tool", "apps", "tool_suggest", "remote_plugin", "browser_use", "computer_use"];

    public CodexRateLimits? RateLimits { get; private set; }

    public event EventHandler<CodexRateLimits>? RateLimitsUpdated;

    public override async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        if (agent.Model is not CodexDesktopModel model) yield break;

        MessageTurn latest = start.OfType<MessageTurn>().Last(m => m.Role == RoleType.User);
        if (latest.Content.FirstOrDefault(c => c is not TextContent and not ImageContent) is IMessageContent unsupported)
            throw new NotSupportedException($"The Codex harness cannot send {unsupported.GetType().Name}.");

        using ImagetoFile images = ImagetoFile.Write(latest.Content.OfType<ImageContent>());
        using Session session = new(this, agent, model);
        Task running = session.RunAsync(latest.Data, images.Paths, CliSystemPrompt(start), token);

        while (await session.Turns.WaitToReadAsync(token).ConfigureAwait(false))
        {
            while (session.Turns.TryRead(out ITurn? turn))
                yield return turn;
        }

        await running.ConfigureAwait(false);
    }

    private void ReportRateLimits(CodexRateLimits limits)
    {
        RateLimits = limits;
        RateLimitsUpdated?.Invoke(this, limits);
    }

    private readonly record struct RpcReply(JsonNode? Result, string? Error);

    private sealed class Session : IDisposable
    {

        private const int ErrorTailLines = 20;
        // Codex's default "auto" never asks about readOnlyHint tools, so Rhino's Ask and argument rules would not run for them.
        private const string AskRhinoEveryCall = "prompt";
        private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(10);

        private CodexHarness Owner { get; }
        private Agent Agent { get; }
        private CodexDesktopModel Model { get; }
        private Process Process { get; }

        private Channel<ITurn> Output { get; } = Channel.CreateUnbounded<ITurn>();
        public ChannelReader<ITurn> Turns => Output.Reader;

        private ConcurrentDictionary<long, TaskCompletionSource<RpcReply>> Pending { get; } = new();
        private SemaphoreSlim WriteGate { get; } = new(1, 1);
        private TaskCompletionSource<ITurn> TurnEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<JsonNode?> SignInCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ConcurrentQueue<string> ErrorTail { get; } = new();
        private Dictionary<string, Mcp> Servers { get; } = new(StringComparer.Ordinal);
        private List<global::Rhino.AI.Mcps.MemoryMcpManager.McpLease> Leases { get; } = [];
        private CodexStreamParser Parser { get; }

        private CancellationToken RunToken { get; set; }
        private string? ThreadId { get; set; }
        private long LastRequestId;

        private bool IsIsolated => !Agent.Config.UseLocalSettings;

        public Session(CodexHarness owner, Agent agent, CodexDesktopModel model)
        {
            Owner = owner;
            Agent = agent;
            Model = model;
            Parser = new(owner, Servers, owner.ReportRateLimits);

            Process = new()
            {
                EnableRaisingEvents = true,
                StartInfo = new()
                {
                    FileName = model.ExePath ?? throw new FileNotFoundException("Codex CLI not found."),
                    WorkingDirectory = owner.Config.CurrentWorkingDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
#if !NET48
            Process.StartInfo.StandardInputEncoding = utf8;
#endif
            Process.StartInfo.StandardOutputEncoding = utf8;
            Process.StartInfo.StandardErrorEncoding = utf8;

            Process.StartInfo.AddArgument("app-server");

            if (IsIsolated)
            {
                Directory.CreateDirectory(CodexDesktopModel.IsolatedCodexHome);
                Process.StartInfo.Environment["CODEX_HOME"] = CodexDesktopModel.IsolatedCodexHome;

                foreach (string feature in DisabledFeatures)
                {
                    Process.StartInfo.AddArgument("--disable");
                    Process.StartInfo.AddArgument(feature);
                }
            }

            Process.ErrorDataReceived += (_, e) => RememberError(e.Data);
        }

        public async Task RunAsync(string prompt, IReadOnlyList<string> imagePaths, string instructions, CancellationToken token)
        {
            RunToken = token;
            try
            {
                Process.Start();
                Process.BeginErrorReadLine();
                _ = ReadLoopAsync();

                await CallAsync("initialize", new JsonObject
                {
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = "rhino",
                        ["title"] = "Rhino",
                        ["version"] = typeof(CodexHarness).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                    },
                }, token).ConfigureAwait(false);
                await SendAsync(new JsonObject { ["method"] = "initialized" }).ConfigureAwait(false);

                if (!await EnsureSignedInAsync(token).ConfigureAwait(false)) return;

                ThreadId = await OpenThreadAsync(instructions, token).ConfigureAwait(false);
                if (ThreadId is null) return;

                await CallAsync("turn/start", TurnParams(ThreadId, prompt, imagePaths), token).ConfigureAwait(false);

                Write(await WithCancellation(TurnEnded.Task, token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or CodexCallException)
            {
                Write(new TurnEnd(StopReason.Error, WithErrorTail(ex.Message)));
            }
            finally
            {
                Output.Writer.TryComplete();
            }
        }

        private async Task<bool> EnsureSignedInAsync(CancellationToken token)
        {
            JsonNode? account = await CallAsync("account/read", new JsonObject(), token).ConfigureAwait(false);
            if (account?["account"] is JsonObject) return true;
            if (account?["requiresOpenaiAuth"]?.GetValue<bool>() == false) return true;

            JsonNode? login = await CallAsync("account/login/start", new JsonObject { ["type"] = "chatgpt" }, token).ConfigureAwait(false);
            if (login?["authUrl"]?.GetValue<string>() is not string url)
            {
                Write(new TurnEnd(StopReason.Error, "Codex did not return a sign-in link."));
                return false;
            }

            Write(new MessageTurn($"Sign in to ChatGPT in your browser to use Codex in Rhino. If no browser opened, use this link: {url}", RoleType.System));
            OpenBrowser(url);

            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(SignInTimeout);

            JsonNode? completed;
            try
            {
                completed = await WithCancellation(SignInCompleted.Task, limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Write(new TurnEnd(StopReason.Error, "Codex sign-in timed out."));
                return false;
            }

            if (completed?["success"]?.GetValue<bool>() == true) return true;

            string reason = completed?["error"]?.GetValue<string>() ?? "Codex stopped before sign-in finished.";
            Write(new TurnEnd(StopReason.Error, $"Codex sign-in failed: {reason}"));
            return false;
        }

        private async Task<string?> OpenThreadAsync(string instructions, CancellationToken token)
        {
            JsonObject parameters = ThreadParams(instructions);
            Guid sessionId = Agent.Config.SessionId;

            if (sessionId == Guid.Empty)
            {
                JsonNode? started = await CallAsync("thread/start", parameters, token).ConfigureAwait(false);
                string? id = started?["thread"]?["id"]?.GetValue<string>();
                if (Guid.TryParse(id, out Guid threadId)) Agent.Config.SessionId = threadId;
                return id;
            }

            parameters["threadId"] = sessionId.ToString();
            RpcReply resumed = await RequestAsync("thread/resume", parameters, token).ConfigureAwait(false);
            if (resumed.Error is string error)
            {
                Write(new TurnEnd(StopReason.InvalidSession, error));
                return null;
            }

            return resumed.Result?["thread"]?["id"]?.GetValue<string>() ?? sessionId.ToString();
        }

        private JsonObject ThreadParams(string instructions)
        {
            JsonObject config = new() { ["mcp_servers"] = McpServers() };
            JsonObject parameters = new()
            {
                ["cwd"] = Owner.Config.CurrentWorkingDirectory,
                ["approvalPolicy"] = "on-request",
                ["developerInstructions"] = instructions,
                ["config"] = config,
            };

            if (!ModelDefinition.IsDefault(Model.Name) && !string.IsNullOrEmpty(Model.Name))
                parameters["model"] = Model.Name;

            if (IsIsolated)
            {
                parameters["sandbox"] = "read-only";
                config["web_search"] = "disabled";
            }

            return parameters;
        }

        private JsonObject TurnParams(string threadId, string prompt, IReadOnlyList<string> imagePaths)
        {
            JsonArray input = new(new JsonObject { ["type"] = "text", ["text"] = prompt });
            foreach (string path in imagePaths)
                input.Add(new JsonObject { ["type"] = "localImage", ["path"] = path });

            JsonObject parameters = new() { ["threadId"] = threadId, ["input"] = input };

            string effort = Agent.Config.ModelEffort;
            if (!string.IsNullOrEmpty(effort) && !string.Equals(effort, "default", StringComparison.OrdinalIgnoreCase))
                parameters["effort"] = effort;

            return parameters;
        }

        private JsonObject McpServers()
        {
            JsonObject servers = new();
            foreach (Mcp mcp in Owner.Mcps.Values)
            {
                if (!mcp.Enabled) continue;

                string[] denied = DeniedTools(mcp.Name);
                if (denied.Contains("*")) continue;

                JsonObject? server = mcp switch
                {
                    StdioMcp stdio => new JsonObject
                    {
                        ["command"] = stdio.Command,
                        ["args"] = new JsonArray([.. stdio.Args.Select(arg => (JsonNode?)JsonValue.Create(arg))]),
                        ["env"] = new JsonObject(stdio.Env.Select(variable => new KeyValuePair<string, JsonNode?>(variable.Key, JsonValue.Create(variable.Value)))),
                        ["default_tools_approval_mode"] = AskRhinoEveryCall,
                    },
                    HttpMcp http => new JsonObject
                    {
                        ["url"] = http.Url.AbsoluteUri,
                        ["http_headers"] = new JsonObject(http.Headers.Select(header => new KeyValuePair<string, JsonNode?>(header.Key, JsonValue.Create(header.Value)))),
                        ["default_tools_approval_mode"] = AskRhinoEveryCall,
                    },
                    MemoryMcp memory => Lease(memory),
                    _ => null,
                };
                if (server is null) continue;

                server["startup_timeout_sec"] = (long)McpTimeouts.Startup.TotalSeconds;
                server["tool_timeout_sec"] = (long)McpTimeouts.ToolCall.TotalSeconds;
                server["disabled_tools"] = new JsonArray([.. denied.Select(tool => (JsonNode?)JsonValue.Create(tool))]);

                string name = ServerName(mcp.Name);
                Servers[name] = mcp;
                servers[name] = server;
            }

            return servers;
        }

        private string[] DeniedTools(string mcpName)
            => [.. AIInstances.Preferences.Permissions.ToolsDeniedForAnyArguments(mcpName)
                .Concat(Agent.Definition?.Permissions.ToolsDeniedForAnyArguments(mcpName) ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)];

        // A leased in-memory MCP already runs Rhino's permission chain per call, so letting Codex ask too would prompt twice.
        private JsonObject Lease(MemoryMcp mcp)
        {
            global::Rhino.AI.Mcps.MemoryMcpManager.McpLease lease = global::Rhino.AI.Mcps.MemoryMcpManager.RegisterMemoryMcp(Agent, Owner, mcp);
            Leases.Add(lease);
            return new JsonObject { ["url"] = lease.Uri.AbsoluteUri, ["default_tools_approval_mode"] = "approve" };
        }

        private static string ServerName(string mcpName)
            => new(mcpName.Select(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' ? c : '_').ToArray());

        private async Task ReadLoopAsync()
        {
            try
            {
                string? line;
                while ((line = await Process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    if (line.Length == 0) continue;

                    JsonNode? message;
                    try
                    {
                        message = JsonNode.Parse(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (message is JsonObject obj) Dispatch(obj);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
            finally
            {
                string reason = WithErrorTail("codex app-server stopped before the turn finished.");
                foreach (long id in Pending.Keys)
                {
                    if (Pending.TryRemove(id, out TaskCompletionSource<RpcReply>? waiter))
                        waiter.TrySetResult(new RpcReply(null, reason));
                }

                SignInCompleted.TrySetResult(null);
                TurnEnded.TrySetResult(new TurnEnd(StopReason.Error, reason));
            }
        }

        private void Dispatch(JsonObject message)
        {
            JsonNode? id = message["id"];

            if (message["method"]?.GetValue<string>() is not string method)
            {
                if (id is JsonValue value && value.TryGetValue(out long requestId) && Pending.TryRemove(requestId, out TaskCompletionSource<RpcReply>? waiter))
                    waiter.TrySetResult(new RpcReply(message["result"], message["error"] is JsonNode error ? ErrorText(error) : null));
                return;
            }

            JsonObject parameters = message["params"] as JsonObject ?? new JsonObject();

            if (id is not null)
            {
                _ = AnswerAsync(id.DeepClone(), method, parameters);
                return;
            }

            OnNotification(method, parameters);
        }

        private void OnNotification(string method, JsonObject parameters)
        {
            if (method == "account/login/completed")
            {
                SignInCompleted.TrySetResult(parameters);
                return;
            }

            switch (Parser.Parse(method, parameters))
            {
                case TurnEnd end:
                    TurnEnded.TrySetResult(end);
                    break;

                case ITurn turn:
                    Write(turn);
                    break;
            }
        }

        private async Task AnswerAsync(JsonNode id, string method, JsonObject parameters)
        {
            JsonObject reply = new() { ["id"] = id };
            try
            {
                JsonNode? result = method switch
                {
                    "mcpServer/elicitation/request" => await ElicitationAsync(parameters).ConfigureAwait(false),
                    "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" => new JsonObject { ["decision"] = "decline" },
                    _ => null,
                };

                if (result is null)
                    reply["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Rhino does not handle {method}." };
                else
                    reply["result"] = result;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                reply["error"] = new JsonObject { ["code"] = -32603, ["message"] = ex.Message };
            }

            try
            {
                await SendAsync(reply).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        }

        private async Task<JsonNode> ElicitationAsync(JsonObject parameters)
        {
            JsonNode? meta = parameters["_meta"];
            bool isToolApproval = meta?["codex_approval_kind"]?.GetValue<string>() == "mcp_tool_call";

            bool allowed = isToolApproval && await IsToolAllowedAsync(
                parameters["serverName"]?.GetValue<string>(),
                meta?["tool_name"]?.GetValue<string>(),
                meta?["tool_params"]).ConfigureAwait(false);

            return allowed
                ? new JsonObject { ["action"] = "accept", ["content"] = new JsonObject() }
                : new JsonObject { ["action"] = "decline" };
        }

        private async Task<bool> IsToolAllowedAsync(string? serverName, string? toolName, JsonNode? toolParams)
        {
            if (serverName is null || toolName is null) return false;
            if (!Servers.TryGetValue(serverName, out Mcp? mcp)) return false;

            List<IToolArg> args = [.. ToolArgs.FromJson(mcp.Tools.GetValueOrDefault(toolName), toolParams)];
            Permissability permission = PermissionSet.ResolvePermissionChain(Agent.Definition, mcp.Name, toolName, args);

            if (permission == Permissability.Deny) return false;
            if (permission != Permissability.Ask || Owner.AskUser is null) return true;

            ToolParameter[] declared = await DeclaredParametersAsync(serverName, toolName).ConfigureAwait(false) ?? [];
            PermissionRequest request = new(Agent, mcp.Name, toolName, Array.ConvertAll(declared, arg => arg.Name), ToolArgs.FromJson(declared, toolParams));
            Owner.RaiseBeforeAskUserPermission(request);
            await Owner.AskUser.Invoke(request, RunToken).ConfigureAwait(false);
            return request.HasPermission;
        }

        // Codex already runs the server, so its tool list saves Rhino starting a second copy just to read the schema.
        private async Task<ToolParameter[]?> DeclaredParametersAsync(string serverName, string toolName)
        {
            JsonNode? cursor = null;
            do
            {
                JsonObject parameters = new() { ["threadId"] = ThreadId };
                if (cursor is not null) parameters["cursor"] = cursor.DeepClone();

                JsonNode? page = await CallAsync("mcpServerStatus/list", parameters, RunToken).ConfigureAwait(false);
                foreach (JsonNode? server in page?["data"] as JsonArray ?? [])
                {
                    if (server?["name"]?.GetValue<string>() == serverName)
                        return ToolSchema.ParseParameters(server["tools"]?[toolName]?["inputSchema"]);
                }

                cursor = page?["nextCursor"];
            }
            while (cursor is not null);

            return null;
        }

        private static string ErrorText(JsonNode error)
            => error["message"]?.GetValue<string>() ?? error.ToJsonString();

        private async Task<JsonNode?> CallAsync(string method, JsonObject parameters, CancellationToken token)
        {
            RpcReply reply = await RequestAsync(method, parameters, token).ConfigureAwait(false);
            if (reply.Error is string error) throw new CodexCallException($"{method}: {error}");
            return reply.Result;
        }

        private async Task<RpcReply> RequestAsync(string method, JsonObject parameters, CancellationToken token)
        {
            long id = Interlocked.Increment(ref LastRequestId);
            TaskCompletionSource<RpcReply> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending[id] = waiter;

            await SendAsync(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }).ConfigureAwait(false);
            return await WithCancellation(waiter.Task, token).ConfigureAwait(false);
        }

        private async Task SendAsync(JsonObject message)
        {
            string line = message.ToJsonString();
            await WriteGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await Process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
                await Process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                WriteGate.Release();
            }
        }

        private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken token)
        {
            TaskCompletionSource<T> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = token.Register(() => cancelled.TrySetCanceled(token));
            Task<T> first = await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false);
            return await first.ConfigureAwait(false);
        }

        private void Write(ITurn turn) => Output.Writer.TryWrite(turn);

        private void RememberError(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            ErrorTail.Enqueue(line!);
            while (ErrorTail.Count > ErrorTailLines && ErrorTail.TryDequeue(out _)) { }
        }

        private string WithErrorTail(string message)
            => ErrorTail.IsEmpty ? message : $"{message}\n{string.Join("\n", ErrorTail)}";

        private static void OpenBrowser(string url)
        {
            try
            {
                using Process? browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException) { }
        }

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException or Win32Exception) { }

            Process.Dispose();
            WriteGate.Dispose();

            foreach (global::Rhino.AI.Mcps.MemoryMcpManager.McpLease lease in Leases)
                lease.Dispose();
            Leases.Clear();
        }

    }

    private sealed class CodexCallException(string message) : Exception(message);

}
