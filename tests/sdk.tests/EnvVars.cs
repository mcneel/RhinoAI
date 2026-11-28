using System.Runtime.CompilerServices;

using Rhino.AI;

namespace sdk.tests;

internal static class EnvVars
{

    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("BYPASS_AI_PERMISSIONS", "true");

        // Ensure ALL Definitions are enabled for the purpose of tests
        foreach(IAgentDefinition def in AIInstances.Agents.Definitions)
        {
            def.Enabled = true;
        }
    }

}
