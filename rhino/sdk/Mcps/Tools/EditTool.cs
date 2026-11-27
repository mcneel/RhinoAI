using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Rhino.AI.Tools;

/// <summary>
/// A tool for replacing text in a file.
/// </summary>
public sealed record EditTool() : Tool("edit",
                                "Used for replacing an exact piece of text in a file on disk. The old text must appear exactly once in the file",
                                false,
                                true,
                                [
                                    new ("path", "The absolute file path", ToolArgType.FilePath, true),
                                    new ("old", "The exact text to replace", ToolArgType.String, true),
                                    new ("new", "The text to replace it with", ToolArgType.String, true),
                                ])
{

    public override async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetPath(Args[0].Name, out string filePath))
            return ToolReturn.MissingArgument(Name, Args[0].Name, "an absolute path");

        if (!args.TryGetString(Args[1].Name, out string oldText))
            return ToolReturn.MissingArgument(Name, Args[1].Name, "the exact text to replace");

        if (!args.TryGetString(Args[2].Name, out string newText))
            return ToolReturn.MissingArgument(Name, Args[2].Name, "the replacement text");

        if (oldText.Length == 0)
            return ToolReturn.Failure("old parameter cannot be empty.", "Use the write tool to create a file from nothing.");

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
            string data = File.ReadAllText(filePath);
#else
            string data = await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false);
#endif

            int start = data.IndexOf(oldText, StringComparison.Ordinal);
            if (start < 0)
                return ToolReturn.Failure("Could not find the old text in the file.", "Read the file first and copy the text to replace out of it exactly, whitespace included.");

            if (data.IndexOf(oldText, start + oldText.Length, StringComparison.Ordinal) >= 0)
                return ToolReturn.Failure("The old text appears more than once.", "Include enough surrounding text to make it unique.");

            string edited = data.Remove(start, oldText.Length).Insert(start, newText);

#if NET48
            File.WriteAllText(filePath, edited);
#else
            await File.WriteAllTextAsync(filePath, edited, token).ConfigureAwait(false);
#endif
            return ToolReturn.Success($"Edited {filePath} successfully");
        }
        catch (UnauthorizedAccessException)
        {
            return ToolReturn.FileAccessDenied("edit");
        }
        catch (Exception ex)
        {
            return ToolReturn.FileError(ex, "edited");
        }
    }

}
