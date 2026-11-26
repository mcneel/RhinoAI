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
    internal static string IsolatedCodexHome { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RhinoAI", "Codex");

    internal static string UserCodexHome =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    internal static IReadOnlyList<string> GeneratedImageFolders(Guid threadId) =>
    [
        Path.Combine(UserCodexHome, "generated_images", threadId.ToString()),
        Path.Combine(IsolatedCodexHome, "generated_images", threadId.ToString()),
    ];

}
