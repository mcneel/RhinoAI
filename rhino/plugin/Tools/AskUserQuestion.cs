namespace RhMcp;

internal enum AskUserMode { Single, Multi }

// Dumb carrier for a posed-but-unanswered question. In the non-blocking model the question is just
// state both channels read to render; the answer arrives as the agent's NEXT prompt, so there is no
// TaskCompletionSource to complete and no first-wins race to coordinate.
internal sealed class PendingQuestion
{
    public PendingQuestion(string question, IReadOnlyList<string> options, AskUserMode mode)
    {
        Question = question;

        // Collapse any agent-supplied literal "Other" so the synthesized free-text affordance
        // never duplicates a real option in either channel.
        List<string> kept = [];
        foreach (string option in options)
            if (!IsOther(option))
                kept.Add(option);
        Options = kept;
        Mode = mode;
    }

    public string Question { get; }
    public IReadOnlyList<string> Options { get; }
    public AskUserMode Mode { get; }

    public static bool IsOther(string label) =>
        string.Equals(label?.Trim(), "Other", StringComparison.OrdinalIgnoreCase);
}
