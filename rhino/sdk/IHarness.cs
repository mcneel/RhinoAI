using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public interface IHarness
{

#region MCP

    public IReadOnlyDictionary<string, IMcp> Mcps { get; }
    
    public IReadOnlyDictionary<string, ISkill> Skills { get; }

#endregion

#region Send/Recieve

    public Task<IEnumerable<ITurn>> SendAsync(IEnumerable<ITurn> turn, CancellationToken token);
    public Task<IEnumerable<ITurn>> LoopAsync(ITurn turn, CancellationToken token);

#endregion

#region Tools

    public Task<ToolReturn> UseToolAsync(string name, List<KeyValuePair<string, string>> args, CancellationToken token);

#endregion

}
