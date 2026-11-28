using System.Text.Json.Nodes;

using Rhino.AI;
using Rhino.AI.Models;
using Rhino.AI.PlugIns;

namespace sdk.tests;

public class ClaudeStreamFailureTests
{

    private static (string, StopReason)[] SignInFailures { get; } =
    [
        ("not-logged-in", StopReason.UnAuthenticated),
        ("invalid-api-key", StopReason.UnAuthenticated),
        ("invalid-oauth-token", StopReason.UnAuthenticated),
        ("invalid-auth-token", StopReason.UnAuthenticated),
        ("oauth-expired", StopReason.UnAuthenticated),
        ("oauth-revoked", StopReason.UnAuthenticated),
        ("api-key-forbidden", StopReason.UnAuthenticated),
        ("org-disallows-subscription", StopReason.Error),
    ];

    private static (string, StopReason)[] ApiFailures { get; } =
    [
        ("credit-too-low", StopReason.Error),
        ("usage-limit-five-hour", StopReason.UsageLimit),
        ("usage-limit-weekly", StopReason.UsageLimit),
        ("rate-limited", StopReason.Error),
        ("overloaded", StopReason.Error),
        ("overloaded-after-retries", StopReason.Error),
        ("server-error", StopReason.Error),
        ("model-not-found", StopReason.Error),
        ("prompt-too-long", StopReason.Error),
        ("request-too-large", StopReason.Error),
        ("connection-refused", StopReason.Error),
    ];

    private static IEnumerable<string> AllFailures => SignInFailures.Concat(ApiFailures).Select(failure => failure.Item1);

    [TestCaseSource(nameof(SignInFailures))]
    public void SignInFailureEndsWithItsReason((string recording, StopReason reason) tuple)
    {
        TurnEnd end = Parse(tuple.recording).OfType<TurnEnd>().Single();

        Assert.That(end.Reason, Is.EqualTo(tuple.reason));
        Assert.That(end.Success, Is.False);
        Assert.That(end.Data, Is.Not.Empty);
    }

    [TestCaseSource(nameof(ApiFailures))]
    public void ApiFailureEndsWithItsReasonAndTheCliText((string recording, StopReason reason) tuple)
    {
        TurnEnd end = Parse(tuple.recording).OfType<TurnEnd>().Single();

        Assert.That(end.Reason, Is.EqualTo(tuple.reason));
        Assert.That(end.Success, Is.False);
        Assert.That(end.Data, Is.EqualTo(ResultText(tuple.recording)));
    }

    [TestCaseSource(nameof(AllFailures))]
    public void FailureIsNotShownAsAnAssistantReply(string recording)
    {
        IEnumerable<MessageTurn> replies = Parse(recording).OfType<MessageTurn>().Where(m => m.Role == RoleType.Assistant);

        Assert.That(replies, Is.Empty);
    }

    [Test, Ignore("TODO")]
    public void MissingSessionEndsAsInvalidSession()
    {
        TurnEnd end = Parse("resume-missing").OfType<TurnEnd>().Single();

        Assert.That(end.Reason, Is.EqualTo(StopReason.InvalidSession));
        Assert.That(end.Success, Is.False);
    }

    private static List<ITurn> Parse(string recording)
    {
        ClaudeStreamParser parser = new(new Harness(PlugInToken.Invalid), Guid.Empty, _ => { });
        return Lines(recording).SelectMany(parser.Parse).ToList();
    }

    private static string ResultText(string recording)
        => Lines(recording)
            .Select(line => JsonNode.Parse(line))
            .Single(node => node?["type"]?.GetValue<string>() == "result")!["result"]!
            .GetValue<string>();

    private static string[] Lines(string recording)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Recordings", "ClaudeCli", recording + ".jsonl"));

}
