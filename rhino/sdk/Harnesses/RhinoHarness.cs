using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public sealed class RhinoHarness : GenericHarness
{

    public RhinoHarness() : base()
    {
        StdioMcp rhinoMcp = new("rhino", new Uri(ResolveRouter));
        AddMcp(rhinoMcp);
    }

    // '/Users/sykes/Library/Application Support/McNeel/Rhinoceros/ai/bin/rhino-mcp-router'
    private string? PrivateResolvedRouter { get; set; }
    public string ResolveRouter
    {
        get
        {
            if (string.IsNullOrEmpty(PrivateResolvedRouter))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string mcneel = Path.Combine(appData, "McNeel");
                string rhinoceros = Path.Combine(mcneel, "Rhinoceros");
                string ai = Path.Combine(rhinoceros, "ai");
                string bin = Path.Combine(ai, "bin");
                string router = Path.Combine(bin, "rhino-mcp-router");
                PrivateResolvedRouter = router;
            }

            return PrivateResolvedRouter;
        }
    }

}
