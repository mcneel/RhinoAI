using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public sealed class RhinoHarness : GenericHarness
{

    public RhinoHarness() : base()
    {
        StdioMcp rhinoMcp = new ("rhino", new Uri(ResolveRouter));
        PrivateMcps.Add(rhinoMcp.Name, rhinoMcp);
    }

    public string ResolveRouter => throw new NotImplementedException("Where is the Router?");

    public override Task<bool> RequestPermissionFromUser(string name, List<IToolArg> args, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public override Task<IEnumerable<ITurn>> SendAsync(IEnumerable<ITurn> turn, CancellationToken token)
    {
        // TODO : Ask the Model!
        throw new NotImplementedException();
    }
    
}
