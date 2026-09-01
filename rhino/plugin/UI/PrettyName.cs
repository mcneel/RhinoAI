using System.Text;

namespace RhinoAI;

// Display-only proper-casing for agent and model identifiers. These are stored lowercase (CLI command
// names, registry keys, --model values) and so read as unpolished in the panel; this raises them to
// "Claude", "Opus", "GPT-5.5" for display only and never feeds back into the stored identity. Case is
// only ever raised, never lowered, so a custom agent the user named with intentional casing survives;
// tokens that look like version numbers (contain a digit) pass through untouched.
internal static class PrettyName
{
    // Standalone tokens uppercased wholesale rather than just initial-capped, so model ids read as
    // "GPT-5.5" instead of "Gpt-5.5".
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase) { "gpt" };

    public static string Of(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        StringBuilder sb = new(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] is ' ' or '-')
            {
                sb.Append(raw[i++]);
                continue;
            }
            int start = i;
            while (i < raw.Length && raw[i] is not (' ' or '-'))
                i++;
            sb.Append(Token(raw[start..i]));
        }
        return sb.ToString();
    }

    private static string Token(string token)
    {
        if (Acronyms.Contains(token))
            return token.ToUpperInvariant();
        if (token.Any(char.IsDigit) || char.IsUpper(token[0]))
            return token;
        return char.ToUpperInvariant(token[0]) + token[1..];
    }
}
