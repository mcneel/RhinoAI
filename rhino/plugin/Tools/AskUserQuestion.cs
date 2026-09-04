namespace RhinoAI;

internal enum AskUserMode { Single, Multi }

// Dumb carrier for a posed-but-unanswered question. In the non-blocking model the question is just
// state both channels read to render; the answer arrives as the agent's NEXT prompt, so there is no
// TaskCompletionSource to complete and no first-wins race to coordinate.
internal sealed class PendingQuestion
{
    public PendingQuestion(string question, IReadOnlyList<string> options, AskUserMode mode)
    {
        Question = question;

        // Collapse agent-supplied duplicates of what the panel synthesizes for every question.
        List<string> kept = [];
        foreach (string option in options)
            if (!IsPanelSynthesized(option))
                kept.Add(option);
        Options = kept;
        Mode = mode;
    }

    public string Question { get; }
    public IReadOnlyList<string> Options { get; }
    public AskUserMode Mode { get; }

    public static bool IsPanelSynthesized(string label) =>
        string.Equals(label?.Trim(), "Other", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(label?.Trim(), "I don't know", StringComparison.OrdinalIgnoreCase);
}
