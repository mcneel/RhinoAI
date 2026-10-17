using System.Diagnostics;
using System.IO;

using Acp;

using Rhino.Runtime;

namespace Rhino.AI;

// Spawns Gemini / Antigravity in native ACP mode and returns a started
// ClientSideConnection driving it.
internal static class GeminiConnection
{
    public static IAcpAgent Connect(AgentDefinition def, IAcpClient client, string cwd)
    {
        if (!CliProcess.TryResolve(def.SearchPaths.GetPaths(), out string path))
            throw new FileNotFoundException("Neither Antigravity (agy.exe) nor Gemini CLI was found on your system.");

        ProcessStartInfo psi = new()
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
        };
        CliProcess.ConfigureEncoding(psi);
        CliProcess.ConfigureFileName(psi, path);

        string fileName = Path.GetFileName(path).ToLowerInvariant();
        if (fileName.Contains("agy"))
        {
            // Antigravity CLI ACP mode
            psi.AddArgument("--acp");
        }
        else
        {
            // Standard Gemini CLI ACP mode
            psi.AddArgument("--acp");
        }

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                HostUtils.LogDebugEvent($"[{def.Name}:err] {e.Data}.\n");
        };
        proc.Start();
        proc.BeginErrorReadLine();

        try
        {
            ProcessStdioTransport transport = new(proc);
            ClientSideConnection connection = new(_ => client, transport);
            connection.Start();
            return connection;
        }
        catch
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process already gone
            }
            proc.Dispose();
            throw;
        }
    }
}