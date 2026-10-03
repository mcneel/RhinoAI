using NUnit.Framework.Constraints;

using Rhino.AI;

public static class Turns
{

    public static Constraint EndsWith(params string[] keywords) => new EndsWithMessageConstraint(keywords);

    private sealed class EndsWithMessageConstraint(string[] keywords) : Constraint
    {

        public override string Description => $"last turn to be a message containing {string.Join(", ", keywords.Select(k => $"\"{k}\""))}";

        public override ConstraintResult ApplyTo<TActual>(TActual actual)
        {
            if (actual is not IEnumerable<ITurn> turns)
                throw new ArgumentException($"Expected IEnumerable<ITurn> but was {actual?.GetType().Name ?? "null"}", nameof(actual));

            ITurn? last = turns.LastOrDefault();
            bool isSuccess = last is MessageTurn message
                && keywords.All(k => message.Message.Contains(k, StringComparison.OrdinalIgnoreCase));

            return new ConstraintResult(this, last, isSuccess);
        }

    }

}
