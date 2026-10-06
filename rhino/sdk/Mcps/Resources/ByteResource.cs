using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

internal sealed record ByteResource(string Key, string Title, string Description, Uri Uri, string MimeType) : McpResource(Key, Title, Description, Uri, MimeType)
{
    public override async Task<ResourceReturn> ReadAsync(CancellationToken token)
    {
        string path = Uri.LocalPath;
        if (!File.Exists(path)) return ResourceReturn.Failure();
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string data = Convert.ToBase64String(bytes);
            return new ResourceReturn(data, true);
        }
        catch (Exception ex)
        {
            return new ResourceReturn(ex.Message, false);
        }
    }
}
