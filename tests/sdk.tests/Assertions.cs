using NUnit.Framework.Constraints;

using Rhino.AI;

public static class Replied
{

    public static Constraint With(params string[] keywords) => new RepliedWithConstraint(keywords);

    private sealed class RepliedWithConstraint(string[] keywords) : Constraint
    {

        public override string Description => $"a reply containing {string.Join(", ", keywords.Select(k => $"\"{k}\""))}";

        public override ConstraintResult ApplyTo<TActual>(TActual actual)
        {
            if (actual is not IAgentResult result)
                throw new ArgumentException($"Expected {nameof(IAgentResult)} but was {actual?.GetType().Name ?? "null"}", nameof(actual));

            bool isSuccess = result.Text is string text
                && keywords.All(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

            return new ConstraintResult(this, result.Text, isSuccess);
        }

    }

}
