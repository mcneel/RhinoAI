using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Acp;

namespace RhMcp;

// An ACP transport over a spawned process's stdio. Owns the process: disposing it (which the
// ClientSideConnection does on its own dispose) kills the process tree, so a closed agent leaves no
// orphaned children.
internal sealed class ProcessStdioTransport : IAcpTransport
{
    private Process Proc { get; }
    private StdioTransport Inner { get; }

    public ProcessStdioTransport(Process proc)
    {
        Proc = proc;
        Inner = new StdioTransport(proc.StandardOutput, proc.StandardInput);
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        Inner.ReadLineAsync(cancellationToken);

    public ValueTask WriteLineAsync(string json, CancellationToken cancellationToken = default) =>
        Inner.WriteLineAsync(json, cancellationToken);

    public void Dispose()
    {
        Inner.Dispose();
        try
        {
            if (!Proc.HasExited)
                Proc.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // process already gone
        }
        Proc.Dispose();
    }
}
