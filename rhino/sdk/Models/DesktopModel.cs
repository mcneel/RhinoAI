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

    protected abstract Task<IEnumerable<ITurn>> SendPrivateAsync(IHarness harness, IEnumerable<ITurn> turns, CancellationToken token);
    
    public async Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turns, CancellationToken token)
    {
        if (!UserPermissions.IsPermitted(Vendor, Name))
            throw new PermissionException("Model or Vendor does not have permission.");
        
        return await SendPrivateAsync(harness, turns, token);
    }
    
}
