namespace RhinoAI.StreamJson.Tests;

// This string is what the agent reads back as the user's next prompt, so its shape is a contract.
[TestFixture]
public sealed class QuestionReplyTests
{
    private static PendingQuestion Question(string text) =>
        new(text, ["a", "b"], AskUserMode.Single);

    [Test]
    public void A_single_question_replies_with_the_bare_answer()
    {
        string reply = QuestionReply.Compose([Question("Which units?")], ["mm"]);

        Assert.That(reply, Is.EqualTo("mm"));
    }

    [Test]
    public void A_batch_labels_every_line_so_answers_map_back()
    {
        string reply = QuestionReply.Compose(
            [Question("Which units?"), Question("Tolerance?"), Question("Where?")],
            ["mm", "0.001", "A new layer"]);

        Assert.That(reply, Is.EqualTo(
            $"Which units? mm{Environment.NewLine}Tolerance? 0.001{Environment.NewLine}Where? A new layer"));
    }

    [Test]
    public void A_multi_select_answer_keeps_its_joined_form()
    {
        string reply = QuestionReply.Compose(
            [Question("Which units?"), Question("Which levels?")],
            ["mm", "L12, L13, L14"]);

        Assert.That(reply, Does.EndWith("Which levels? L12, L13, L14"));
    }

    [Test]
    public void A_single_question_with_no_answer_composes_to_empty()
    {
        string reply = QuestionReply.Compose([Question("Which units?")], []);

        Assert.That(reply, Is.Empty);
    }

    [Test]
    public void Answers_running_short_of_the_questions_stop_rather_than_throw()
    {
        string reply = QuestionReply.Compose(
            [Question("Which units?"), Question("Tolerance?")],
            ["mm"]);

        Assert.That(reply, Is.EqualTo("Which units? mm"));
    }
}
