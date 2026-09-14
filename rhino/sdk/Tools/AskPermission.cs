using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Rhino.AI.Tools;

public class AskPermission : ITool
{

    public string Name { get; } = "ask_permissions";
    public string Description { get; } = "Used when you need to ask the user for permission";
    public bool ReadOnly { get; } = false;
    public bool Destructive { get; } = true;    
    public ToolArg[] Args { get; } = [
        new ToolArg("permission", "The absolute file path", ToolArgType.Choice, [""]),
    ];

    public async Task<ToolReturn> UseAsync(IReadOnlyDictionary<string, object> args, CancellationToken token)
    {
        if (!args.TryGetAs(Args[0].Name, out string filePath))
            return ToolReturn.Failure("file parameter is mandatory", "Call edit again with file set to an absolute path.");

        if (!args.TryGetAs(Args[1].Name, out string oldText))
            return ToolReturn.Failure("old parameter is mandatory", "Call edit again with old set to the exact text to replace.");

        if (!args.TryGetAs(Args[2].Name, out string newText))
            return ToolReturn.Failure("new parameter is mandatory", "Call edit again with new set to the replacement text.");

        if (oldText.Length == 0)
            return ToolReturn.Failure("old parameter cannot be empty.", "Use the write tool to create a file from nothing.");

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
            string data = await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false);

            int start = data.IndexOf(oldText, StringComparison.Ordinal);
            if (start < 0)
                return ToolReturn.Failure("Could not find the old text in the file.", "Read the file first and copy the text to replace out of it exactly, whitespace included.");

            if (data.IndexOf(oldText, start + oldText.Length, StringComparison.Ordinal) >= 0)
                return ToolReturn.Failure("The old text appears more than once.", "Include enough surrounding text to make it unique.");

            string edited = string.Concat(data.AsSpan(0, start), newText, data.AsSpan(start + oldText.Length));

            await File.WriteAllTextAsync(filePath, edited, token).ConfigureAwait(false);
            return ToolReturn.Success($"Edited {filePath} successfully");
        }
        catch (UnauthorizedAccessException)
        {
            return ToolReturn.Failure("Did not have permission to edit file", "Ask the user to grant access, or edit a different file.");
        }
        catch (Exception ex)
        {
            return ToolReturn.Failure(ex.Message, "The file could not be edited. Do not retry the same call unchanged.");
        }
    }

}
