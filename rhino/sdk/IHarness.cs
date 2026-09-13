using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public interface IHarness
{

    // public bool RegisterTool();

    public Task<IEnumerable<ITurn>> SendAsync(IEnumerable<ITurn> turn, CancellationToken token);
    public Task<IEnumerable<ITurn>> LoopAsync(ITurn turn, CancellationToken token);
    public ToolResult UseTool(string name, List<KeyValuePair<string, string>> args);

    // MCP Servers

}
