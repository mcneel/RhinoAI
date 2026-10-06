using System;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

/// <summary>A Resource inside of an MCP</summary>
public interface IResource
{

    /// <summary>The uri of the resource</summary>
    public Uri Uri { get; }

    /// <summary>The key to the resource</summary>
    public string Key { get; }

    /// <summary>Human readable name</summary>
    public string Title { get; }

    /// <summary>A description of the tool</summary>
    public string Description { get; }

    /// <summary>The Resource Type</summary>
    public string MimeType { get; }

    /// <summary>
    /// A call to read the resource
    /// </summary>
    /// <param name="args">The arguments for the tool call</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>The <see cref="ToolReturn"/></returns>
    public Task<ResourceReturn> ReadAsync(CancellationToken token);

}

/// <summary>
/// Return from a Resource read call
/// </summary>
/// <param name="Data"></param>
/// <param name="Success"></param>
public sealed record ResourceReturn(string Data, bool Success)
{
    public static ResourceReturn Failure() => new("", false);
}
