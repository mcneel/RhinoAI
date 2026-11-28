using System.Linq;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>A Claude Desktop Model</summary>
internal sealed class ClaudeDesktopModel(string name, IEnumerable<string> searchPaths) : DesktopModel(name, "Anthropic", searchPaths)
{

    public override bool Available => ExePath is not null;

    internal string? ExePath => SearchPaths.FirstOrDefault();

}
