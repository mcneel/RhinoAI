using Rhino.AI;
using Rhino.AI.Models;
using Rhino.AI.PlugIns;

namespace sdk.tests;

internal sealed class TestDefinition() : AgentDefinition("Test", "Test Def")
{

    public override bool Available => true;

    public override Agent? GetAgent(PlugInToken token)
        => new(this, token, new Harness(token));

    public override IModel? GetModel(string modelName) => new TestModel() { Name = modelName };

    public override Agent? WithModel(PlugInToken token, string modelName)
    {
        throw new NotImplementedException();
    }

    public sealed class TestModel : IModel
    {
        public string Name { get; set; } = "Test";

        public string Vendor { get; set; } = "RhinoAI";

        public bool Available { get; set; } = true;

        public Func<Harness, IEnumerable<ITurn>, CancellationToken, Task<IEnumerable<ITurn>>>? SendAsyncFunc { get; set; }
        public Task<IEnumerable<ITurn>> SendAsync(Harness harness, IEnumerable<ITurn> turns, CancellationToken token)
            => SendAsyncFunc!.Invoke(harness, turns, token);

        public Func<Harness, IEnumerable<ITurn>, CancellationToken, IAsyncEnumerable<ITurn>>? StreamAsyncFunc { get; set; }

        public IAsyncEnumerable<ITurn> StreamAsync(Harness harness, IEnumerable<ITurn> turn, CancellationToken token)
            => StreamAsyncFunc!.Invoke(harness, turn, token);

    }

}
