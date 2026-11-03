using System;
using System.IO;
using System.Collections.Generic;

namespace Rhino.AI.Models;

/// <summary>A Claude Desktop Model</summary>
internal sealed class ClaudeDesktopModel(string name, IEnumerable<string> searchPaths) : DesktopModel(name, "Anthropic", searchPaths)
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
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude");
        }
    }

}
