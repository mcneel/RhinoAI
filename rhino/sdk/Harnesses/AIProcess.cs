using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.ComponentModel;
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
        if (e.Data.Contains("No conversation found with session ID", StringComparison.OrdinalIgnoreCase))
        {
            Turns.Enqueue(new TurnEnd(StopReason.InvalidSession, e.Data));
            return;
        }

        Turns.Enqueue(new MessageTurn(e.Data, RoleType.System, DateTime.UtcNow) { Success = false });
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

    public void Start()
    {
        if (!Process.Start())
            throw new InvalidOperationException($"{Process.StartInfo.FileName} did not start a new process.");

        Process.BeginErrorReadLine();
    }

    public void Kill()
    {
        try
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
        }
        // Never started, exited between the check and the kill, or the OS refused part of the tree.
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or Win32Exception) { }
    }

    public void Dispose()
    {
        Kill();
        Process.Dispose();
    }

    public async Task WaitForExitAsync(CancellationToken token) => await Process.WaitForExitAsync(token);

}
