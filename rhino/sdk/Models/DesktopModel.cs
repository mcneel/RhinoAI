using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal abstract class DesktopModel(string name, string vendor) : IModel
{

    public string Name { get; } = name;
    public string Vendor { get; } = vendor;

    public abstract bool Available { get; }
    
    public async Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turns, CancellationToken token)
    {
        throw new NotImplementedException($"Do not call {nameof(SendAsync)} on desktop Models. Use the Agent.");
    }
    
}
