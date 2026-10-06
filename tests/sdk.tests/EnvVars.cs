using System.Runtime.CompilerServices;

namespace sdk.tests;

internal static class EnvVars
{

    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("BYPASS_AI_PERMISSIONS", "true");
    }

}
