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
                                    new ("file", "The absolute file path", ToolArgType.FilePath, true),
                                    new ("data", "The data to write", ToolArgType.String, true),
                                ])
{

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetPath(Args[0].Name, out string filePath))
            return ToolReturn.Failure("file parameter is mandatory", "Call write again with file set to an absolute path.");

        if (!args.TryGetString(Args[1].Name, out string data))
            return ToolReturn.Failure("data parameter is mandatory", "Call write again with data set to the whole contents of the file.");

        if (Directory.Exists(filePath))
            return ToolReturn.Failure("File is a directory.", "Pass the path of a file, not a folder.");

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (directory is not null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(filePath, data, token).ConfigureAwait(false);
            return ToolReturn.Success($"Wrote {data.Length} characters to {filePath}");
        }
        catch (UnauthorizedAccessException)
        {
            return ToolReturn.Failure("Did not have permission to write file", "Ask the user to grant access, or write to a different path.");
        }
        catch (Exception ex)
        {
            return ToolReturn.Failure(ex.Message, "The file could not be written. Do not retry the same call unchanged.");
        }
    }

}
