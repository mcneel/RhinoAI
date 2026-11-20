using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

using System.Threading.Tasks;
using System.IO;

namespace Rhino.AI;

/// <summary>An MCP for Rhino Plugins</summary>
public sealed class RhinoMcp(string name) : StdioMcp(name, new Uri(ResolvedRouter))
{

    // '/Users/sykes/Library/Application Support/McNeel/Rhinoceros/ai/bin/rhino-mcp-router'
    private static string? CachedResolvedRouter { get; set; }
    public static string ResolvedRouter
    {
        get
        {
            if (string.IsNullOrEmpty(CachedResolvedRouter))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string mcneel = Path.Combine(appData, "McNeel");
                string rhinoceros = Path.Combine(mcneel, "Rhinoceros");
                string ai = Path.Combine(rhinoceros, "ai");
                string bin = Path.Combine(ai, "bin");
                string router = Path.Combine(bin, "rhino-mcp-router");
                CachedResolvedRouter = router;
            }

            return CachedResolvedRouter;
        }
    }

}

public abstract record RhinoTool : ITool
{
    public string Name { get; }

    public string Description { get; }

    public bool ReadOnly { get; }

    public bool Destructive { get; }

    public ToolParameter[] Args { get; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    private RhinoTool() {}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public RhinoTool(string name, string description, bool readOnly, bool destructive, ToolParameter[] args)
    {
        Name = name;
        Description = description;
        ReadOnly = readOnly;
        Destructive = destructive;
        
        // Ensure RhinoDoc is included in the Args list
        List<ToolParameter> argList = args.ToList();
        argList.Insert(0, new ToolParameter("RhinoDoc", "The Current Rhino Document", ToolArgType.Object, true));
        Args = args.ToArray();
    }

    public abstract Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token);

}
