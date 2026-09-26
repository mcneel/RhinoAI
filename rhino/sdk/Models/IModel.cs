using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>
/// An AI Model
/// </summary>
public interface IModel
{
    
    /// <summary>The Name of the Model</summary>
    public string Name { get; }

    /// <summary>The Vendor of the Model</summary>
    public string Vendor { get; }

    /// <summary>Is the Model available?</summary>
    public bool Available { get; }
    
    /// <summary>Send a series of messages to the Model</summary>
    /// <param name="harness">The Harness to use with the model</param>
    /// <param name="turn">The messages to start the conversation with</param>
    /// <param name="token">A Cancellation token</param>
    /// <returns>The response from the AI Model</returns>
    public Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turn, CancellationToken token);
    
}
