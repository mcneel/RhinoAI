using System.Diagnostics;
using System.Text;

namespace Rhino.AI;

internal static class ProcessArguments
{
    public static void AddArgument(this ProcessStartInfo psi, string argument)
    {
#if NETFRAMEWORK
        StringBuilder line = new(psi.Arguments);
        if (line.Length != 0)
            line.Append(' ');
        AppendEscaped(line, argument);
        psi.Arguments = line.ToString();
#else
        psi.ArgumentList.Add(argument);
#endif
    }

#if NETFRAMEWORK
    private static void AppendEscaped(StringBuilder line, string argument)
    {
        line.Append('"');
        int i = 0;
        while (i < argument.Length)
        {
            char c = argument[i++];
            if (c == '\\')
            {
                int backslashes = 1;
                while (i < argument.Length && argument[i] == '\\')
                {
                    i++;
                    backslashes++;
                }

                if (i == argument.Length)
                    line.Append('\\', backslashes * 2);
                else if (argument[i] == '"')
                {
                    line.Append('\\', backslashes * 2 + 1).Append('"');
                    i++;
                }
                else
                    line.Append('\\', backslashes);
            }
            else if (c == '"')
                line.Append('\\').Append('"');
            else
                line.Append(c);
        }
        line.Append('"');
    }
#endif
}
