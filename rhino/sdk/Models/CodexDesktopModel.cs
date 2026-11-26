using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>A Codex Desktop Model</summary>
internal sealed class CodexDesktopModel(string name, IEnumerable<string> searchPaths) : DesktopModel(name, "OpenAI", searchPaths)
{

    public override bool Available => ExePath is not null;

    internal string? ExePath => SearchPaths.FirstOrDefault();

    // app-server has no --ignore-user-config, so a separate CODEX_HOME is the only clean way to keep ~/.codex out of Rhino sessions.
    internal string IsolatedCodexHome { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McNeel", "Rhinoceros", "ai", "codex");

}
