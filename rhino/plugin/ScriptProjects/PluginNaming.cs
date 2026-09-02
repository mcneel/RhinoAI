using System;
using System.Text;
using System.Text.RegularExpressions;

namespace RhinoAI.ScriptProjects;

// Nothing here may touch RhinoCommon: this file is linked into Server.Tests, which is Rhino-free.
internal static class PluginNaming
{
    public const string PluginSuffix = "PlugIn";
    public const string FallbackPluginName = "RhinoAI" + PluginSuffix;

    private const int MaxCommandNameLength = 64;

    public static PluginCommandAction TryParseAction(string? action)
        => action?.Trim().ToLowerInvariant() switch
        {
            "add" => PluginCommandAction.Add,
            "update" => PluginCommandAction.Update,
            "delete" => PluginCommandAction.Delete,
            _ => PluginCommandAction.None
        };

    public static CommandNameProblem TryCoerceCommandName(ref string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CommandNameProblem.Empty;

        name = name.Replace(" ", "_");

        string tempName = string.Empty;
        foreach (char character in name)
        {
            if (!IsCommandNameCharacter(character))
                continue;
            tempName += character;
        }

        name = tempName;

        if (name!.Length > MaxCommandNameLength)
            return CommandNameProblem.TooLong;

        return CommandNameProblem.None;
    }

    public static CommandNameProblem ValidateCommandName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CommandNameProblem.Empty;

        if (name!.Length > MaxCommandNameLength)
            return CommandNameProblem.TooLong;

        foreach (char character in name)
        {
            if (!IsCommandNameCharacter(character))
                return CommandNameProblem.InvalidCharacters;
        }

        return CommandNameProblem.None;
    }

    public static string Describe(CommandNameProblem problem, string? name) => problem switch
    {
        CommandNameProblem.Empty => "Command name is required.",
        CommandNameProblem.InvalidCharacters => $"Command name \"{name}\" is not valid. Use letters, digits and underscores only, with no spaces.",
        CommandNameProblem.TooLong => $"Command name \"{name}\" is longer than {MaxCommandNameLength} characters.",

        _ => $"Unknown problem. {nameof(problem)}",
    };

    public static string? GetPlugInName(string? requested)
    {
        List<string?> possibilities = [requested, AISettings.ScriptPluginName];

        try
        {
            Match match = Regex.Match(RhinoApp.LoggedInUserName, @"([\S\s]+) - [\S]+@");
            string userName = match.Groups[1].Value.Replace(" ", "");
            possibilities.Add(userName);
        }
        catch { }

        possibilities.Add(Environment.UserName);

        foreach(string? possibility in possibilities)
        {
            string santised = SanitisePluginName(possibility);
            if (string.IsNullOrEmpty(santised)) continue;
            return santised;
        }

        return string.Empty;
    }

    public static string SanitisePluginName(string? requested)
        => ToAsciiPascalCase(requested);

    private static string ToAsciiPascalCase(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        StringBuilder builder = new(raw!.Length);
        bool capitaliseNext = true;

        foreach (char character in raw)
        {
            if (IsAsciiLetterOrDigit(character))
            {
                builder.Append(capitaliseNext ? char.ToUpperInvariant(character) : character);
                capitaliseNext = false;
            }
            else
                capitaliseNext = true;
        }

        if (builder.Length == 0)
            return string.Empty;

        if (char.IsDigit(builder[0]))
            builder.Insert(0, 'P');

        return builder.ToString();
    }

    private static bool IsCommandNameCharacter(char character) =>
        IsAsciiLetterOrDigit(character) || character == '_';

    private static bool IsAsciiLetterOrDigit(char character) =>
        (character >= 'a' && character <= 'z')
        || (character >= 'A' && character <= 'Z')
        || (character >= '0' && character <= '9');

}

internal enum PluginCommandAction { None = 0, Add, Update, Delete }

internal enum CommandNameProblem { None, Empty, InvalidCharacters, TooLong }
