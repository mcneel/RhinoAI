using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Grasshopper2.Doc;

namespace Rhino.AI.Resources;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum GH2DiagnosticLevel
{
    Remark,
    Warning,
    Error,
    Fault,
}

internal sealed record GH2Diagnostic(Guid Id, string Name, string Nickname, GH2DiagnosticLevel Level, string Message);

internal sealed record GH2SolveSummary(bool Solved, int Objects, string Phase, int Errors, int Warnings, GH2Diagnostic[] Diagnostics);

internal static class GH2_Diagnostics
{

    public static TimeSpan SolveTimeout { get; } = TimeSpan.FromSeconds(60);

    public static async Task<GH2SolveSummary> SolveAsync(Document ghDoc)
    {
        using CancellationTokenSource source = new(SolveTimeout);

        Solution? solution = null;
        try
        {
            solution = await ghDoc.Solution.Start(source, SolutionMode.Regular).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return Faulted(ghDoc, "Canceled", $"The solution did not finish within {SolveTimeout.TotalSeconds:F0} seconds and was canceled");
        }
        catch (Exception ex)
        {
            return Faulted(ghDoc, "Faulted", ex.Message);
        }

        List<GH2Diagnostic> collected = Collect(ghDoc);
        (int errors, int warnings) = Count(collected);

        return new GH2SolveSummary(
            errors == 0,
            ghDoc.Objects.Count,
            "Completed",
            errors,
            warnings,
            collected.ToArray());
    }

    private static GH2SolveSummary Faulted(Document ghDoc, string phase, string message) =>
        new(false, ghDoc.Objects.Count, phase, 1, 0,
            [new GH2Diagnostic(Guid.Empty, "Solution", "", GH2DiagnosticLevel.Fault, message)]);

    public static List<GH2Diagnostic> Collect(Document ghDoc)
    {
        List<GH2Diagnostic> diagnostics = [];

        foreach (IDocumentObject obj in ghDoc.Objects.ActiveObjects)
        {
            ObjectSolutionState state = obj.State;

            if (state.Phase == Phase.Faulted)
            {
                string text = state.FaultException?.Message ?? "Component faulted during solve";
                diagnostics.Add(MakeDiagnostic(obj, GH2DiagnosticLevel.Fault, text));
                continue;
            }

            if (state.Phase == Phase.Cancelled)
            {
                diagnostics.Add(MakeDiagnostic(obj, GH2DiagnosticLevel.Error,
                    "Skipped: a prior fault canceled this solution before this component ran"));
                continue;
            }

            SolutionData? data = state.Data;
            if (data is null)
                continue;

            Messages messages = data.Messages;
            for (int i = 0; i < messages.Count; i++)
            {
                Message m = messages[i];
                if (!TryMapLevel(m.Level, out GH2DiagnosticLevel level))
                    continue;
                diagnostics.Add(MakeDiagnostic(obj, level, m.Text));
            }
        }

        return diagnostics;
    }

    // Roll a diagnostic list up to (errors, warnings). Faults and Cancelled-as-Error
    // both count as errors so callers keep Solved false and keep self-correcting.
    public static (int Errors, int Warnings) Count(IReadOnlyList<GH2Diagnostic> diagnostics)
    {
        int errors = 0;
        int warnings = 0;
        foreach (GH2Diagnostic d in diagnostics)
        {
            if (d.Level is GH2DiagnosticLevel.Error or GH2DiagnosticLevel.Fault)
                errors++;
            else if (d.Level is GH2DiagnosticLevel.Warning)
                warnings++;
        }
        return (errors, warnings);
    }

    private static GH2Diagnostic MakeDiagnostic(IDocumentObject obj, GH2DiagnosticLevel level, string message) =>
        new(obj.InstanceId, obj.Nomen.Name, obj.UserName ?? "", level, message);

    private static bool TryMapLevel(MessageLevel level, out GH2DiagnosticLevel mapped)
    {
        switch (level)
        {
            case MessageLevel.Remark:
                mapped = GH2DiagnosticLevel.Remark;
                return true;
            case MessageLevel.Warning:
                mapped = GH2DiagnosticLevel.Warning;
                return true;
            case MessageLevel.Error:
                mapped = GH2DiagnosticLevel.Error;
                return true;
            default:
                mapped = GH2DiagnosticLevel.Remark;
                return false;
        }
    }
}
