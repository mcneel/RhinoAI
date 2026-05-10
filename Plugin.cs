using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{

    public RhMcpPlugin()
    {
        Instance = this;
    }

#pragma warning disable
    public static RhMcpPlugin Instance { get; private set; }
#pragma warning enable


}
