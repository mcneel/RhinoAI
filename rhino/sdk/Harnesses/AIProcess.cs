using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Rhino.AI;

internal class AIProcess : IDisposable
{

    public Process Process { get; }

    private ConcurrentQueue<ITurn> Turns { get; } = [];
    public StreamWriter StandardInput => Process.StandardInput;
    public StreamReader StandardOutput => Process.StandardOutput;

    public int ExitCode => Process.ExitCode;

    public AIProcess(Process process)
    {
        Process = process;

        process.ErrorDataReceived += ReadErrors;
    }

    private void ReadErrors(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        Turns.Enqueue(new MessageTurn(e.Data, RoleType.System, DateTime.UtcNow));
    }

    public bool TryPop(out ITurn turn) => Turns.TryDequeue(out turn!) && turn is not null;

    public bool TryPop<TTurn>(out TTurn turn) where TTurn : ITurn
    {
        turn = default!;
        if (!Turns.TryDequeue(out ITurn? unCast)) return false;
        if (unCast is not TTurn castTurn) return false;
        turn = castTurn;
        return true;
    }

    public void Dispose()
    {
        Process.Dispose();
    }

    internal void Kill(bool entireProcessTree) => Process.Kill(entireProcessTree);

    public async Task WaitForExitAsync(CancellationToken token) => await Process.WaitForExitAsync(token);

}
