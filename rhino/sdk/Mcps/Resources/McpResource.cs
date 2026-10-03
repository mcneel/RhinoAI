using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public abstract record McpResource(string Key, string Title, string Description, Uri Uri, string MimeType) : IResource
{
    public abstract Task<ResourceReturn> ReadAsync(CancellationToken token);

    public static IResource? TryCreateResourceFromUri(string key, string title, string description, Uri uri)
    {
        string path = uri.LocalPath;
        string? extension = Path.GetExtension(path)?.ToLowerInvariant();
        return extension switch
        {
            ".md" => new TextResource(key, title, description, uri, "text/markdown"),
            ".txt" or ".log" => new TextResource(key, title, description, uri, "text/plain"),
            ".json" => new TextResource(key, title, description, uri, "application/json"),
            // TODO : Should .ghx be here?
            ".xml" => new TextResource(key, title, description, uri, "application/xml"),
            ".csv" => new TextResource(key, title, description, uri, "text/csv"),
            ".html" => new TextResource(key, title, description, uri, "text/html"),
            ".yaml" or ".yml" => new TextResource(key, title, description, uri, "application/yaml"),
            ".svg" => new TextResource(key, title, description, uri, "image/svg+xml"),
            ".cs" => new TextResource(key, title, description, uri, "text/x-csharp"),
            ".py" => new TextResource(key, title, description, uri, "text/x-python"),
            ".jpg" or ".jpeg" => new ByteResource(key, title, description, uri, "image/jpeg"),
            ".gif" => new ByteResource(key, title, description, uri, "image/gif"),
            ".webp" => new ByteResource(key, title, description, uri, "image/webp"),
            ".bmp" => new ByteResource(key, title, description, uri, "image/bmp"),
            ".pdf" => new ByteResource(key, title, description, uri, "application/pdf"),
            
            _ => new ByteResource(key, title, description, uri, "application/octet-stream")
        };
    }

}
