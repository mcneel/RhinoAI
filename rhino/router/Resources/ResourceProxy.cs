using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

using RhMcp.Server;

namespace RhMcp.Router.Resources;

// Aggregates MCP resources for the router. Three responsibilities:
//
//   1. Serve router-local built-ins (mcp/**/*.md next to the binary) directly
//      from disk — these are reachable even with no slots running.
//   2. Fan out `resources/list` (and templates) to every Ready slot,
//      dedupe by URI, populate the URI → slot cache so subsequent reads can
//      route directly.
//   3. Route `resources/read` via the cache; fall back to a sequential
//      broadcast across remaining slots if the cache-owner is gone.
//
// Per-slot timeouts: 1 s for list, 5 s for read. Failures are swallowed —
// the agent sees the partial result rather than an error.
internal sealed class ResourceProxy(
    RhinoManager manager,
    IHttpClientFactory httpFactory,
    RouterBuiltInResources builtIns,
    ResourceRouteCache cache,
    ILogger<ResourceProxy> log)
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private const string RouterLocalSlotId = "<router>";

    public async Task<ListResourcesResult> ListAsync(CancellationToken ct)
    {
        IReadOnlyCollection<ChildRhino> slots = manager.List();

        // Merge order: slot results in (resource, slotId) tuples first, then
        // router-local overlays. First-write wins on URI for slot fan-out;
        // router-local always overrides (built-in primer takes precedence).
        Dictionary<string, (Resource Resource, string SlotId)> merged =
            new(StringComparer.Ordinal);

        if (slots.Count > 0)
        {
            Task<List<Resource>>[] tasks = slots
                .Select(s => SafeListSlotAsync(s, ct))
                .ToArray();
            List<Resource>[] perSlot = await Task.WhenAll(tasks).ConfigureAwait(false);

            for (int i = 0; i < perSlot.Length; i++)
            {
                ChildRhino slot = slots.ElementAt(i);
                foreach (Resource r in perSlot[i])
                {
                    if (!merged.ContainsKey(r.Uri))
                        merged[r.Uri] = (r, slot.SlotId);
                }
            }
        }

        foreach (PluginResource b in builtIns.All)
        {
            merged[b.Uri] = (ToProtocolResource(b), RouterLocalSlotId);
        }

        cache.Replace(merged.Select(kv =>
            new KeyValuePair<string, string>(kv.Key, kv.Value.SlotId)));

        return new ListResourcesResult
        {
            Resources = merged.Values.Select(v => v.Resource).ToList(),
        };
    }

    public async Task<ListResourceTemplatesResult> ListTemplatesAsync(CancellationToken ct)
    {
        IReadOnlyCollection<ChildRhino> slots = manager.List();
        if (slots.Count == 0)
            return new ListResourceTemplatesResult { ResourceTemplates = new List<ResourceTemplate>() };

        Task<List<ResourceTemplate>>[] tasks = slots
            .Select(s => SafeListTemplatesAsync(s, ct))
            .ToArray();
        List<ResourceTemplate>[] perSlot = await Task.WhenAll(tasks).ConfigureAwait(false);

        Dictionary<string, ResourceTemplate> merged = new(StringComparer.Ordinal);
        foreach (List<ResourceTemplate> batch in perSlot)
        {
            foreach (ResourceTemplate t in batch)
            {
                if (!merged.ContainsKey(t.UriTemplate))
                    merged[t.UriTemplate] = t;
            }
        }

        return new ListResourceTemplatesResult
        {
            ResourceTemplates = merged.Values.ToList(),
        };
    }

    public async Task<ReadResourceResult> ReadAsync(string uri, CancellationToken ct)
    {
        // 1. Router-local hit — serve from disk directly.
        if (builtIns.MatchByUri(uri) is { } local)
            return await ReadLocalAsync(local, ct).ConfigureAwait(false);

        // 2. Cache hit — try the slot the last list call said owned this URI.
        if (cache.TryGetSlotId(uri, out string ownerSlotId) &&
            manager.Get(ownerSlotId) is { } owner)
        {
            try
            {
                return await CallReadAsync(owner, uri, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Cache-owner slot '{Slot}' couldn't serve resource '{Uri}', falling back to broadcast",
                    ownerSlotId, uri);
            }
        }

        // 3. Broadcast fallback — try every other live slot sequentially.
        foreach (ChildRhino slot in manager.List())
        {
            if (cache.TryGetSlotId(uri, out string already) && string.Equals(already, slot.SlotId, StringComparison.Ordinal))
                continue; // already tried this one above

            try
            {
                return await CallReadAsync(slot, uri, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Slot '{Slot}' couldn't serve resource '{Uri}' during broadcast", slot.SlotId, uri);
            }
        }

        throw new InvalidOperationException($"No slot could serve resource '{uri}'.");
    }

    // ---------------- private helpers ----------------

    private static Resource ToProtocolResource(PluginResource r) => new()
    {
        Uri = r.Uri,
        Name = r.Name,
        Description = r.Description,
        MimeType = r.MimeType,
    };

    private static async Task<ReadResourceResult> ReadLocalAsync(PluginResource r, CancellationToken ct)
    {
        string text;
        if (r.IsIndex)
        {
            text = r.IndexBody ?? "";
        }
        else if (r.FilePath is { } path)
        {
            text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        else
        {
            text = "";
        }

        return new ReadResourceResult
        {
            Contents = new List<ResourceContents>
            {
                new TextResourceContents { Uri = r.Uri, MimeType = r.MimeType, Text = text },
            },
        };
    }

    private async Task<List<Resource>> SafeListSlotAsync(ChildRhino slot, CancellationToken ct)
    {
        try
        {
            JsonElement result = await CallSlotAsync(slot, "resources/list", paramsNode: null, ListTimeout, ct)
                .ConfigureAwait(false);
            return ParseResources(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            log.LogDebug("Slot '{Slot}' timed out during resources/list", slot.SlotId);
            return new List<Resource>();
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Slot '{Slot}' connection failed during resources/list", slot.SlotId);
            manager.TryReapDead(slot.SlotId);
            return new List<Resource>();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Slot '{Slot}' returned an unexpected error during resources/list", slot.SlotId);
            return new List<Resource>();
        }
    }

    private async Task<List<ResourceTemplate>> SafeListTemplatesAsync(ChildRhino slot, CancellationToken ct)
    {
        try
        {
            JsonElement result = await CallSlotAsync(slot, "resources/templates/list", paramsNode: null, ListTimeout, ct)
                .ConfigureAwait(false);
            return ParseResourceTemplates(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            log.LogDebug("Slot '{Slot}' timed out during resources/templates/list", slot.SlotId);
            return new List<ResourceTemplate>();
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Slot '{Slot}' connection failed during resources/templates/list", slot.SlotId);
            manager.TryReapDead(slot.SlotId);
            return new List<ResourceTemplate>();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Slot '{Slot}' returned an unexpected error during resources/templates/list", slot.SlotId);
            return new List<ResourceTemplate>();
        }
    }

    private async Task<ReadResourceResult> CallReadAsync(ChildRhino slot, string uri, CancellationToken ct)
    {
        JsonObject paramsNode = new() { ["uri"] = uri };
        JsonElement result = await CallSlotAsync(slot, "resources/read", paramsNode, ReadTimeout, ct)
            .ConfigureAwait(false);
        return ParseReadResult(result, uri);
    }

    private async Task<JsonElement> CallSlotAsync(
        ChildRhino slot, string method, JsonNode? paramsNode, TimeSpan timeout, CancellationToken ct)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        // Build the envelope as a JsonObject so we can include `params` as a
        // raw JsonNode without going through RouterJsonContext (which is
        // tool-call-shaped). Method names and IDs are server-controlled, so
        // string interpolation would be safe too — the JsonObject is just
        // tidier and handles future param shapes uniformly.
        string requestId = Guid.NewGuid().ToString("N");
        JsonObject envelope = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["method"] = method,
        };
        if (paramsNode is not null)
            envelope["params"] = paramsNode;
        string json = envelope.ToJsonString();

        log.LogDebug("Proxying {Method} to slot '{Slot}' at {Endpoint}", method, slot.SlotId, slot.Endpoint);

        HttpClient http = httpFactory.CreateClient();
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpRequestMessage request = new(HttpMethod.Post, slot.Endpoint + "/")
        {
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using HttpResponseMessage response = await http.SendAsync(
            request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Slot '{slot.SlotId}' returned HTTP {(int)response.StatusCode} for {method}: {body}");
        }

        return JsonRpcOverHttp.ExtractResult(body, slot.SlotId, method);
    }

    // ---------------- JSON → protocol DTOs ----------------

    private static List<Resource> ParseResources(JsonElement result)
    {
        List<Resource> list = new();
        if (result.ValueKind != JsonValueKind.Object) return list;
        if (!result.TryGetProperty("resources", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (JsonElement item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string? uri = TryGetString(item, "uri");
            string? name = TryGetString(item, "name");
            if (uri is null || name is null) continue;

            list.Add(new Resource
            {
                Uri = uri,
                Name = name,
                Description = TryGetString(item, "description"),
                MimeType = TryGetString(item, "mimeType"),
            });
        }
        return list;
    }

    private static List<ResourceTemplate> ParseResourceTemplates(JsonElement result)
    {
        List<ResourceTemplate> list = new();
        if (result.ValueKind != JsonValueKind.Object) return list;
        if (!result.TryGetProperty("resourceTemplates", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (JsonElement item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string? uriTemplate = TryGetString(item, "uriTemplate");
            string? name = TryGetString(item, "name");
            if (uriTemplate is null || name is null) continue;

            list.Add(new ResourceTemplate
            {
                UriTemplate = uriTemplate,
                Name = name,
                Description = TryGetString(item, "description"),
                MimeType = TryGetString(item, "mimeType"),
            });
        }
        return list;
    }

    private static ReadResourceResult ParseReadResult(JsonElement result, string uri)
    {
        List<ResourceContents> contents = new();
        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("contents", out JsonElement arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string itemUri = TryGetString(item, "uri") ?? uri;
                string? mime = TryGetString(item, "mimeType");
                string? text = TryGetString(item, "text");
                string? blob = TryGetString(item, "blob");

                if (text is not null)
                {
                    contents.Add(new TextResourceContents { Uri = itemUri, MimeType = mime, Text = text });
                }
                else if (blob is not null)
                {
                    // The wire form is base64; the SDK's BlobResourceContents
                    // holds the raw bytes (its serializer re-encodes on the way
                    // back out).
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(blob); }
                    catch (FormatException) { continue; }
                    contents.Add(new BlobResourceContents { Uri = itemUri, MimeType = mime, Blob = bytes });
                }
            }
        }
        return new ReadResourceResult { Contents = contents };
    }

    private static string? TryGetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
