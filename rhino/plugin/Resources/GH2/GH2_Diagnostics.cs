using System.Text.Json.Serialization;

using Grasshopper2.Doc;

namespace RhMcp.Resources;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GH2DiagnosticLevel
{
    Remark,
    Warning,
    Error,
    Fault,
}

public sealed record GH2Diagnostic(Guid Id, string Name, string Nickname, GH2DiagnosticLevel Level, string Message);

public static class GH2_Diagnostics
{
    // The post-solve diagnostic surface lives on IDocumentObject.State, which is
    // an ObjectSolutionState (always present, never null). Three distinct sources
    // matter and the old code only read the first:
    //   1. State.Data.Messages    - the per-object Messages collection (Remark/
    //      Warning/Error). Messages is not IEnumerable; it exposes Count + an
    //      indexer + WorstMessageLevel, hence the index loop below.
    //   2. State.Phase == Faulted - a component that threw during Compute records
    //      no Message; the only signal is the Faulted phase plus FaultException.
    //      Without this branch a hard component crash reads back as "solved, no
    //      messages", which is exactly the case the self-correct loop must catch.
    //      A faulted object keeps its PREVIOUS solve's Data (Fault() carries the
    //      old Data, not the exception), so we report only the fault and skip its
    //      stale Messages, otherwise old remarks/warnings bleed into the report.
    //   3. State.Phase == Cancelled - when one object faults, the scheduler cancels
    //      the document solution token and every still-pending object throws
    //      OperationCanceledException, landing in Cancelled with stale Data and
    //      never recomputed. Without this branch all downstream components that
    //      never ran are silently dropped, so a multi-fault solve under-reports
    //      and the loop wrongly declares partial success. Surface them as Errors so
    //      the count reflects actual state and the caller keeps iterating.
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
                    "Skipped: a prior fault cancelled this solution before this component ran"));
                continue;
            }

            SolutionData? data = state.Data;
            if (data is null) continue;

            Messages messages = data.Messages;
            for (int i = 0; i < messages.Count; i++)
            {
                Message m = messages[i];
                if (!TryMapLevel(m.Level, out GH2DiagnosticLevel level)) continue;
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
            if (d.Level is GH2DiagnosticLevel.Error or GH2DiagnosticLevel.Fault) errors++;
            else if (d.Level is GH2DiagnosticLevel.Warning) warnings++;
        }
        return (errors, warnings);
    }

    private static GH2Diagnostic MakeDiagnostic(IDocumentObject obj, GH2DiagnosticLevel level, string message) =>
        new(obj.InstanceId, obj.Nomen.Name, obj.UserName ?? "", level, message);

    private static bool TryMapLevel(MessageLevel level, out GH2DiagnosticLevel mapped)
    {
        switch (level)
        {
            case MessageLevel.Remark: mapped = GH2DiagnosticLevel.Remark; return true;
            case MessageLevel.Warning: mapped = GH2DiagnosticLevel.Warning; return true;
            case MessageLevel.Error: mapped = GH2DiagnosticLevel.Error; return true;
            default: mapped = GH2DiagnosticLevel.Remark; return false;
        }
    }
}
