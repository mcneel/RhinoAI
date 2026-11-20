using System.Runtime.CompilerServices;

using Rhino.AI;

namespace sdk.tests;

internal static class EnvVars
{

    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("BYPASS_AI_PERMISSIONS", "true");
    }

}
