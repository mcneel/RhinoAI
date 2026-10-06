using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Rhino.AI.Models;

internal abstract class DesktopModel(string name, string vendor) : IModel
{

    public string Name { get; } = name;
    public string Vendor { get; } = vendor;

    public abstract bool Available { get; }
    
    public async IAsyncEnumerable<ITurn> StreamAsync(IHarness harness, IEnumerable<ITurn> turns, [EnumeratorCancellation]CancellationToken token)
    {
        throw new NotImplementedException($"Do not call {nameof(StreamAsync)} on desktop Models. Use the Agent.");

        // required for code to compile
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162 // Unreachable code detected
    }
    
    public async Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turns, CancellationToken token)
    {
        throw new NotImplementedException($"Do not call {nameof(SendAsync)} on desktop Models. Use the Agent.");
    }
    
}
