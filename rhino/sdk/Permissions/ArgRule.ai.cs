using System;
using System.Globalization;

namespace Rhino.AI;

internal enum RuleOperator { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual }

internal readonly record struct ArgRule(RuleOperator Operator, string Operand)
{

    public const string Anything = "*";
    public const string Nothing = "!!";

    private static (string Token, RuleOperator Operator)[] Tokens { get; } =
    [
        ("==", RuleOperator.Equal),
        ("!=", RuleOperator.NotEqual),
        ("<=", RuleOperator.LessThanOrEqual),
        (">=", RuleOperator.GreaterThanOrEqual),
        ("<", RuleOperator.LessThan),
        (">", RuleOperator.GreaterThan),
        ("=", RuleOperator.Equal),
    ];

    public static ArgRule Parse(string rule)
    {
        string trimmed = rule.Trim();

        foreach ((string token, RuleOperator ruleOperator) in Tokens)
        {
            if (trimmed.StartsWith(token, StringComparison.Ordinal))
                return new ArgRule(ruleOperator, trimmed[token.Length..].TrimStart());
        }

        return new ArgRule(RuleOperator.Equal, trimmed);
    }

    public bool MatchesBoolean(bool value)
    {
        if (!bool.TryParse(Operand, out bool expected)) return false;
        return MatchesEquality(value == expected);
    }

    public bool MatchesNumber(double value)
    {
        if (!double.TryParse(Operand, NumberStyles.Float, CultureInfo.InvariantCulture, out double expected)) return false;

        return Operator switch
        {
            RuleOperator.Equal => value == expected,
            RuleOperator.NotEqual => value != expected,
            RuleOperator.LessThan => value < expected,
            RuleOperator.LessThanOrEqual => value <= expected,
            RuleOperator.GreaterThan => value > expected,
            RuleOperator.GreaterThanOrEqual => value >= expected,
            _ => throw new ArgumentOutOfRangeException(nameof(Operator), Operator, "Unknown rule operator."),
        };
    }

    public bool MatchesText(string value) => MatchesEquality(string.Equals(value, Operand, StringComparison.Ordinal));

    public bool MatchesPath(string value) =>
        MatchesGlob(new GlobPattern(NormalizePath(Operand), '/', !PathsAreCaseSensitive), NormalizePath(value));

    public bool MatchesUrl(string value) => MatchesGlob(new GlobPattern(Operand, '/', true), value);

    private bool MatchesGlob(GlobPattern pattern, string value) => MatchesEquality(pattern.IsMatch(value));

    private bool MatchesEquality(bool areEqual) => Operator switch
    {
        RuleOperator.Equal => areEqual,
        RuleOperator.NotEqual => !areEqual,
        RuleOperator.LessThan or RuleOperator.LessThanOrEqual or RuleOperator.GreaterThan or RuleOperator.GreaterThanOrEqual => false,
        _ => throw new ArgumentOutOfRangeException(nameof(Operator), Operator, "Unknown rule operator."),
    };

    // macOS is case insensitive like Windows, so only Linux tells "Model.3dm" and "model.3dm" apart.
    private static bool PathsAreCaseSensitive => OperatingSystem.IsLinux();

    private static string NormalizePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("~/", StringComparison.Ordinal)) return normalized;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/') + normalized[1..];
    }

}
