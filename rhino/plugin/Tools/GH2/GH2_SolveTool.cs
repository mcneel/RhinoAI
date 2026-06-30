using RhMcp.Resources;

using Grasshopper2.Doc;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH2_SolveTool
{
    public sealed record SolveResult(bool Solved, string Phase, int Errors, int Warnings, GH2Diagnostic[] Diagnostics);
    public sealed record SolveError(bool Solved, string Error);

    [McpServerTool("g2_solve_canvas", "Solve GH2 Canvas", false, false)]
    [Description("Solves the active GH2 canvas and reads back per-component diagnostics. Returns {Solved, Phase, Errors, Warnings, Diagnostics[]}. Each diagnostic is {Id, Name, Nickname, Level (Remark|Warning|Error|Fault), Message}. Solved is true only when the solution completed with no Error or Fault. Use this to see exactly which components failed and why, then fix them.")]
    public static string SolveCanvas(RhinoDoc rhDoc)
    {
        if (!GH2_Utils.TryGetDoc(rhDoc, out Document ghDoc))
            return JsonSerializer.Serialize(new SolveError(false, "Could not get GH2 document"));

        Solution solution;
        try
        {
            solution = ghDoc.Solution.StartWait();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new SolveError(false, ex.Message));
        }

        List<GH2Diagnostic> diagnostics = GH2_Diagnostics.Collect(ghDoc);
        (int errors, int warnings) = GH2_Diagnostics.Count(diagnostics);

        bool solved = solution.Phase == SolutionPhase.Completed && errors == 0;

        return JsonSerializer.Serialize(new SolveResult(
            solved,
            solution.Phase.ToString(),
            errors,
            warnings,
            diagnostics.ToArray()));
    }
}
