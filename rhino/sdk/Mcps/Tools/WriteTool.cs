using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Rhino.AI.Tools;

/// <summary>
/// An MCP tool for writing a file to disk. This tool will overwrite any existing file in its place.
/// </summary>
public sealed record WriteTool() : Tool("write",
                                "Used for writing a file to disk. Creates the file if it does not exist, and overwrites it whole if it does",
                                false,
                                true,
                                [
                                    new ("path", "The absolute file path", ToolArgType.FilePath, true),
                                    new ("data", "The data to write", ToolArgType.String, true),
                                ])
{

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetPath(Args[0].Name, out string filePath))
            return ToolReturn.MissingArgument(Name, Args[0].Name, "an absolute path");

        if (!args.TryGetString(Args[1].Name, out string data))
            return ToolReturn.MissingArgument(Name, Args[1].Name, "the whole contents of the file");

        if (Directory.Exists(filePath))
            return ToolReturn.FileIsDirectory();

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (directory is not null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

#if NET48
            File.WriteAllText(filePath, data);
#else
            await File.WriteAllTextAsync(filePath, data, token).ConfigureAwait(false);
#endif
            return ToolReturn.Success($"Wrote {data.Length} characters to {filePath}");
        }
        catch (UnauthorizedAccessException)
        {
            return ToolReturn.FileAccessDenied("write");
        }
        catch (Exception ex)
        {
            return ToolReturn.FileError(ex, "written");
        }
    }

}
