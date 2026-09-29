using System;
using System.IO;

namespace Rhino.AI.Models;

/// <summary>A Claude Desktop Model</summary>
internal sealed class ClaudeDesktopModel(string name) : DesktopModel(name, "Anthropic")
{

    public override bool Available => File.Exists(ExePath);

    // TODO : Check Definitions.json
    internal static string ExePath
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude");

}
