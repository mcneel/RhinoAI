using System;
using System.IO;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>A Codex Desktop Model</summary>
internal sealed class CodexDesktopModel(string name, IEnumerable<string> searchPaths) : DesktopModel(name, "OpenAI", searchPaths)
{

    public override bool Available => File.Exists(ExePath);

    internal string ExePath
    {
        get
        {
            foreach (string path in SearchPaths)
            {
                // TODO : Globs!
                if (File.Exists(path)) return path;
            }

            // TODO : Add better win default
            return Path.Combine("/Applications", "ChatGPT.app", "Contents", "Resources", "codex");
        }
    }

}
