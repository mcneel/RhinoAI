using System;
using System.IO;

namespace Rhino.AI.Models;

/// <summary>A Codex Desktop Model</summary>
internal sealed class CodexDesktopModel(string name) : DesktopModel(name, "OpenAI")
{

    public override bool Available => File.Exists(ExePath);

    // TODO : Check Definitions.json
    internal static string ExePath
        => System.IO.Path.Combine("/Applications", "ChatGPT.app", "Contents", "Resources", "codex");

}
