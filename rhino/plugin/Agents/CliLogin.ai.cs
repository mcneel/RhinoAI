using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

// CLI-owned authentication: status probes and a separate hidden login process. The login command
// opens the browser itself; the panel reports completion without exposing a terminal window.
internal static class CliLogin
{
    // Deliberately tri-state: only a CLI that SAYS it is signed out starts browser sign-in. A probe
    // that timed out, crashed, or answered in a shape we don't know is Unknown, and Unknown leaves
    // the caller on its ordinary error path rather than sending the user off to sign in for nothing.
    public enum State
    {
        Unknown,
        SignedIn,
        SignedOut,
    }

    // Long enough for a cold CLI start on a busy machine, short enough that a hung probe cannot
    // hold up the prompt it runs in front of.
    private static TimeSpan ProbeTimeout { get; } = TimeSpan.FromSeconds(10);

    // Run the status command hidden and hand its output to the CLI's own reader. Every failure mode
    // (missing binary, timeout, non-zero exit with nothing to read) lands on Unknown by design.
    public static async Task<State> ProbeAsync(string cliPath, IReadOnlyList<string> statusArguments, Func<string, int, State> read)
    {
        if (cliPath.Length == 0 || statusArguments.Count == 0)
            return State.Unknown;

        ProcessStartInfo psi = new()
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        CliProcess.ConfigureEncoding(psi);
        CliProcess.ConfigureFileName(psi, cliPath);
        foreach (string argument in statusArguments)
            psi.AddArgument(argument);

        try
        {
            using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("no process");
            using CancellationTokenSource timeout = new(ProbeTimeout);

            // Read both pipes before waiting: a status command that filled one of them would
            // otherwise block on a full buffer and be killed as a false timeout.
            Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderr = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            return read(await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false), proc.ExitCode);
        }
        catch (Exception)
        {
            return State.Unknown;
        }
    }

    // Keep login separate from the agent process, but hidden. Drain both pipes so CLI output cannot
    // block completion; do not copy authentication URLs/codes into logs or persisted transcripts.
    public static async Task SignInAsync(string cliPath, IReadOnlyList<string> loginArguments, CancellationToken cancellationToken)
    {
        if (cliPath.Length == 0)
            throw new InvalidOperationException("The CLI path is not known yet.");
        if (loginArguments.Count == 0)
            throw new InvalidOperationException("This CLI has no sign-in command.");

        ProcessStartInfo psi = new()
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        CliProcess.ConfigureEncoding(psi);
        CliProcess.ConfigureFileName(psi, cliPath);
        foreach (string argument in loginArguments)
            psi.AddArgument(argument);

        cancellationToken.ThrowIfCancellationRequested();
        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start sign-in.");
        // Browser login needs no console input. EOF lets a CLI that requires manual input fail
        // instead of leaving an invisible prompt waiting forever.
        proc.StandardInput.Close();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        Task stdout = proc.StandardOutput.BaseStream.CopyToAsync(System.IO.Stream.Null, timeout.Token);
        Task stderr = proc.StandardError.BaseStream.CopyToAsync(System.IO.Stream.Null, timeout.Token);
        try
        {
            await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (proc.ExitCode != 0)
                throw new InvalidOperationException("The browser sign-in did not complete.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Sign-in timed out.");
        }
        finally
        {
            // Disposal alone does not stop a process. Closing a document or timing out must also
            // release the login callback listener, so the next /login can start cleanly.
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync().ConfigureAwait(false);
            }
            try
            {
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The wait above reports cancellation/timeout; observe cancelled pipe reads too.
            }
        }
    }
}
