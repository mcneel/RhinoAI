using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Rhino.AI.Tools;

public class ReadTool : ITool
{

    public string Name { get; } = "read";
    public string Description { get; } = "Used for reading a file on disk";
    public bool ReadOnly { get; } = true;
    public bool Destructive { get; } = false;
    public ToolArg[] Args { get; } = [
        new ToolArg("file", "The absolute file path", ToolArgType.FilePath, true),
    ];

    public ReadTool()
    {

    }


    public async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetPath(Args[0].Name, out string filePath))
            return ToolReturn.Failure("file parameter is mandatory", "Call read again with file set to an absolute path.");

        FileInfo info = new(filePath);

        if (!info.Exists)
        {
            DirectoryInfo dInfo = new(filePath);
            if (dInfo.Exists)
                return ToolReturn.Failure("File is a directory.", "Pass the path of a file, not a folder.");

            return ToolReturn.Failure("File does not exist.", "Check the path, or create the file with the write tool first.");
        }

        try
        {
            return ToolReturn.Success(await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false));
        }
        catch (UnauthorizedAccessException)
        {
            return ToolReturn.Failure("Did not have permission to read file", "Ask the user to grant access, or read a different file.");
        }
        catch (Exception ex)
        {
            return ToolReturn.Failure(ex.Message, "The file could not be read. Do not retry the same call unchanged.");
        }
    }

}
