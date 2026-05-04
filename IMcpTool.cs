using System.Text.Json.Nodes;

namespace RhMcp;

public interface IMcpTool
{
    public string Name { get; }
    public string Description { get; }
    public object InputSchema { get; }
    public object Execute(JsonObject? args);
}
