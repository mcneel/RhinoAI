#if R9
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal static class ModelsFetcher
{
    private static TimeSpan FetchTimeout { get; } = TimeSpan.FromSeconds(8);

    private static HttpClient Client { get; } = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = 16 * 1024 * 1024,
    };

    public static async Task<ModelsResult> FetchAsync(string host, string modelsRoute, CancellationToken cancellation)
    {
        string address = host.Trim().TrimEnd('/') + "/" + modelsRoute.Trim().TrimStart('/');
        if (HttpAddress.Parse(address) is not Uri uri)
            return Failed(LOC.STR("\"{0}\" is not a valid address."), address);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(FetchTimeout);
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(uri, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed(LOC.STR("{0} answered with status {1}."), address, (int)response.StatusCode);

            using Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(body, default, deadline.Token).ConfigureAwait(false);
            return Interpret(document.RootElement, address);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return Failed(LOC.STR("{0} did not answer in time. Check that the server is running."), address);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Failed(LOC.STR("Could not reach {0}. Check that the server is running."), address);
        }
        catch (JsonException)
        {
            return Failed(LOC.STR("{0} did not answer with a list of models."), address);
        }
    }

    private static ModelsResult Interpret(JsonElement root, string address)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Array)
            return Failed(LOC.STR("{0} did not answer with a list of models."), address);

        List<ModelEntry> models = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonElement item in data.EnumerateArray())
        {
            if (TextOf(item, "id") is not string id || !seen.Add(id))
                continue;
            models.Add(new ModelEntry(id, TextOf(item, "display_name") ?? TextOf(item, "name") ?? id));
        }

        return models.Count == 0
            ? Failed(LOC.STR("{0} did not list any models."), address)
            : new ModelsFound(models);
    }

    private static string? TextOf(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
            return null;

        string text = value.GetString()?.Trim() ?? string.Empty;
        return text.Length > 0 ? text : null;
    }

    private static ModelsFailed Failed(string template, params object[] args) => new(string.Format(template, args));
}
#endif
