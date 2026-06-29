using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Acp;

namespace RhMcp;

// Spawns Gemini in its native ACP mode (`gemini --experimental-acp`) and returns a started
// ClientSideConnection driving it. This is the proof that the rhino/acp library works against a
// real ACP peer, no translator needed, since Gemini speaks ACP directly.
internal static class GeminiConnection
{
    public static IAcpAgent Connect(AgentDefinition def, IAcpClient client, string cwd)
    {
        if (!TryResolveCommand(def.SearchPaths, out string path))
            throw new FileNotFoundException("Gemini CLI not found. Install it (npm i -g @google/gemini-cli).");

        ProcessStartInfo psi = new()
        {
            FileName = path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
        };
        psi.ArgumentList.Add("--experimental-acp");
        foreach (string arg in def.ExtraArgs)
            psi.ArgumentList.Add(arg);

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                RhinoApp.WriteLine($"[{def.Name}:err] {e.Data}");
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
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // process already gone
            }
            proc.Dispose();
            throw;
        }
    }

    // First match wins: SearchPaths leads with PATH, the authoritative source. Identical semantics
    // to StreamJsonAgent.TryResolveCommand so both agents resolve the same binary off the same list.
    private static bool TryResolveCommand(IReadOnlyList<string> searchPaths, out string path)
    {
        foreach (string candidate in searchPaths)
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        path = string.Empty;
        return false;
    }
}
