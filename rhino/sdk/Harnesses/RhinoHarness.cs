using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

/// <summary>
/// A Rhino specific harness, including all Rhino tooling
/// </summary>
public sealed class RhinoHarness : GenericHarness
{

    public RhinoHarness() : base()
    {
        StdioMcp rhinoMcp = new("rhino", new Uri(ResolveRouter));
        AddMcp(rhinoMcp);
    }

    // '/Users/sykes/Library/Application Support/McNeel/Rhinoceros/ai/bin/rhino-mcp-router'
    private string? CachedResolvedRouter { get; set; }
    private string ResolveRouter
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
