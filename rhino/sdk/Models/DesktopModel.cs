using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Models;

public abstract class DesktopModel(string name, string vendor) : IModel
{

    public string Name { get; } = name;
    public string Vendor { get; } = vendor;

    public abstract Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turn, CancellationToken token);
    
}
