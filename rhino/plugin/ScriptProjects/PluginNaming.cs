using System;
using System.Text;

namespace RhinoAI.ScriptProjects;

// Nothing here may touch RhinoCommon: this file is linked into Server.Tests, which is Rhino-free.
internal static class PluginNaming
{
    public const string PluginSuffix = "PlugIn";
    public const string FallbackPluginName = "RhinoAI" + PluginSuffix;

    private const int MaxCommandNameLength = 64;

    public static bool TryParseAction(string? action, out PluginCommandAction parsed)
    {
        parsed = action?.Trim().ToLowerInvariant() switch
        {
            "add" => PluginCommandAction.Add,
            "update" => PluginCommandAction.Update,
            "delete" => PluginCommandAction.Delete,
            _ => PluginCommandAction.None
        };

        return parsed is not PluginCommandAction.None;
    }

    public static CommandNameProblem TryCoerceCommandName(ref string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CommandNameProblem.Empty;

        StringBuilder coerced = new(name.Length);
        foreach (char character in name)
        {
            char candidate = character == ' ' ? '_' : character;
            if (IsCommandNameCharacter(candidate))
                coerced.Append(candidate);
        }

        name = coerced.ToString();

        return ValidateCommandName(name);
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
        _ => throw new ArgumentOutOfRangeException(
            nameof(problem), problem, "A usable command name has no problem to describe."),
    };

    // An explicit override is taken as given: no suffix appended.
    public static string SanitisePluginName(string? requested)
        => TrySanitisePluginName(requested, out string name) ? name : FallbackPluginName;

    public static string PluginNameFor(string? userName)
        => TrySanitisePluginName(userName, out string name) ? name + PluginSuffix : FallbackPluginName;

    public static bool TrySanitisePluginName(string? raw, out string name)
    {
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

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
            return false;

        if (char.IsDigit(builder[0]))
            builder.Insert(0, 'P');

        name = builder.ToString();
        return true;
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
