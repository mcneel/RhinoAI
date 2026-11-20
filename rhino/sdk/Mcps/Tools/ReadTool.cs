using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Rhino.AI.Tools;

/// <summary>
/// An MCP tool to read files from disk
/// </summary>
public sealed record ReadTool() : Tool("read",
                                "Used for reading a file on disk",
                                true,
                                false,
                                [
                                    new ("file", "The absolute file path", ToolArgType.FilePath, true),
                                ])
{

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetPath(Args[0].Name, out string filePath))
            return ToolReturn.MissingArgument(Name, Args[0].Name, "an absolute path");

        FileInfo info = new(filePath);

        if (!info.Exists)
        {
            DirectoryInfo dInfo = new(filePath);
            if (dInfo.Exists)
                return ToolReturn.FileIsDirectory();

            return ToolReturn.FileNotFound();
        }

        try
        {
#if NET48
            return ToolReturn.Success(File.ReadAllText(filePath));
#else
            return ToolReturn.Success(await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false));
#endif
        }
        catch (UnauthorizedAccessException)
        {
            return ToolReturn.FileAccessDenied("read");
        }
        catch (Exception ex)
        {
            return ToolReturn.FileError(ex, "read");
        }
    }

}
