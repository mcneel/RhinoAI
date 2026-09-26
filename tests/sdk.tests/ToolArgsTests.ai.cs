// using Rhino.AI;
// using Rhino.AI.Models;

// namespace sdk.tests;

// public class ToolArgsTests
// {

//     private sealed class ScriptedModel(params IEnumerable<ITurn>[] replies) : IModel
//     {
//         public string Name => "scripted";

//         public string Vendor => "test";

//         public bool Available => true;

//         private Queue<IEnumerable<ITurn>> Replies { get; } = new(replies);

//         public Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turns, CancellationToken token)
//             => Task.FromResult(Replies.Dequeue());
//     }

//     [TestCase("{\"Shmargle Arg")]
//     [TestCase("{\"Shmargle\" Bloogle}")]
//     [TestCase("[1, 2]")]
//     public void MalformedTextIsKept(string text)
//     {
//         ToolTurn turn = ToolArgs.FromText(new GenericHarness(), "call", "Bloogle", text);

//         Assert.That(turn.Args, Is.Empty);
//         Assert.That(turn.UnparsedArgs, Is.EqualTo(text));
//     }

//     [TestCase(null)]
//     [TestCase("")]
//     [TestCase("null")]
//     [TestCase("{\"Shmargle Arg\": \"x\"}")]
//     public void WellFormedTextParses(string? text)
//     {
//         ToolTurn turn = ToolArgs.FromText(new GenericHarness(), "call", "Bloogle", text);

//         Assert.That(turn.UnparsedArgs, Is.Null);
//     }

//     [Test, CancelAfter(5000)]
//     public async Task MalformedCallIsAnsweredWithFailure(CancellationToken token)
//     {
//         ToolTurn call = ToolArgs.FromText(new GenericHarness(), "call", "Bloogle", "{\"Shmargle Arg");
//         ScriptedModel model = new([call], [new MessageTurn("ok", RoleType.Assistant)]);
//         Agent agent = new(model, new GenericHarness(), "Be helpful.");

//         IEnumerable<ITurn> turns = await agent.SendAsync("What is the bloogle?", token);

//         ToolResultTurn result = turns.OfType<ToolResultTurn>().Single();
//         Assert.That(result.Id, Is.EqualTo("call"));
//         Assert.That(result.Return.Result, Is.EqualTo(ToolResult.Failure));
//         Assert.That(turns.Last(), Is.InstanceOf<MessageTurn>());
//     }

// }
