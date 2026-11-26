using System.Linq;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>A Codex Desktop Model</summary>
internal sealed class CodexDesktopModel(string name, IEnumerable<string> searchPaths) : DesktopModel(name, "OpenAI", searchPaths)
{

    public override bool Available => ExePath is not null;

    internal string? ExePath => SearchPaths.FirstOrDefault();

}
