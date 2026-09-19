using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Models;

public interface IModel
{

    public string Name { get; }
    public string Vendor { get; }
    
    public Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turn, CancellationToken token);
    
}
