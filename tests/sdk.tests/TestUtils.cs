using Rhino.AI;

namespace sdk.tests;

internal class TestUtils
{

    public record TestTool(string Name, string Description, ToolParameter[] Args) : ITool
    {
        public bool ReadOnly => true;

        public bool Destructive => false;

        public Func<IReadOnlyList<IToolArg>, CancellationToken, Task<ToolReturn>>? Func { get; set; }

        public async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
        {
            Task<ToolReturn>? task = (Func?.Invoke(args, token)) ?? throw new NotImplementedException("Misshing Func!");
            return await task;
        }

    }

}
