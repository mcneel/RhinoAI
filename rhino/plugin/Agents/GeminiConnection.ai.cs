using System.Diagnostics;
using System.IO;

using Acp;

using Rhino.Runtime;

namespace Rhino.AI;

// Spawns Gemini in its native ACP mode (`gemini --experimental-acp`) and returns a started
// ClientSideConnection driving it. This is the proof that the rhino/acp library works against a
// real ACP peer, no translator needed, since Gemini speaks ACP directly.
internal static class GeminiConnection
{
    public static IAcpAgent Connect(AgentDefinition def, IAcpClient client, string cwd)
    {
        if (!CliProcess.TryResolve(def.SearchPaths.GetPaths(), out string path))
            throw new FileNotFoundException("Gemini CLI not found. Install it (npm i -g @google/gemini-cli).");

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
        psi.AddArgument("--experimental-acp");
        // foreach (string arg in def.ExtraArgs)
        //     psi.AddArgument(arg);

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                HostUtils.LogDebugEvent($"[{def.Name}:err] {e.Data}.\n");
        };
        proc.Start();
        proc.BeginErrorReadLine();

        // Ownership of proc transfers to ProcessStdioTransport.Dispose only once the connection is
        // returned; any throw before that (transport ctor, client factory, connection.Start) would
        // otherwise orphan the spawned process, so kill it and rethrow.
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
                // process already gone
            }
            proc.Dispose();
            throw;
        }
    }
}
