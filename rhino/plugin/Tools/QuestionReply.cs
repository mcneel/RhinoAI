using System.Text;

namespace RhinoAI;

// A batch labels each line so the agent can map answers back to the questions it asked.
internal static class QuestionReply
{
    public static string Compose(IReadOnlyList<PendingQuestion> questions, IReadOnlyList<string> answers)
    {
        if (questions.Count == 1)
            return answers.Count > 0 ? answers[0] : string.Empty;

        StringBuilder sb = new();
        for (int i = 0; i < questions.Count && i < answers.Count; i++)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(questions[i].Question).Append(' ').Append(answers[i]);
        }
        return sb.ToString();
    }
}
