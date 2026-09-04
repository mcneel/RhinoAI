namespace RhinoAI.StreamJson.Tests;

// The panel synthesizes "Other" and "I don't know" on every question, so an agent listing them too must not produce two of each.
[TestFixture]
public sealed class PendingQuestionTests
{
    [Test]
    public void Agent_supplied_synthesized_labels_are_collapsed()
    {
        PendingQuestion question = new(
            "Which units?",
            ["mm", "Other", "m", "I don't know"],
            AskUserMode.Single);

        Assert.That(question.Options, Is.EqualTo(new[] { "mm", "m" }));
    }

    [TestCase("other")]
    [TestCase("  Other  ")]
    [TestCase("I DON'T KNOW")]
    [TestCase(" i don't know ")]
    public void Collapsing_ignores_case_and_surrounding_space(string label)
    {
        PendingQuestion question = new("Which units?", ["mm", label], AskUserMode.Single);

        Assert.That(question.Options, Is.EqualTo(new[] { "mm" }));
    }

    [Test]
    public void A_question_left_with_no_real_options_collapses_to_empty()
    {
        PendingQuestion question = new("Which units?", ["Other", "I don't know"], AskUserMode.Multi);

        Assert.That(question.Options, Is.Empty);
    }

    [Test]
    public void Real_options_keep_their_order_and_their_text()
    {
        PendingQuestion question = new(
            "Which levels?",
            ["L12", "Another option", "L14"],
            AskUserMode.Multi);

        Assert.That(question.Options, Is.EqualTo(new[] { "L12", "Another option", "L14" }));
    }
}
