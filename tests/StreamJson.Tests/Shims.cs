using System;
using System.Collections.Generic;

// Headless stand-ins for the few RhinoCommon / plugin-bootstrap surfaces the compiled-in agent
// sources reach for. They reproduce exactly the API the code under test calls (no more), so the
// real StreamJsonAgent / ConversationStore / parsers run unchanged in a RhinoCommon-free assembly.

namespace Rhino
{
    // StreamJsonAgent's only RhinoCommon dependency is the err/parse-error log line. A no-op sink is
    // faithful: the design mandates the parsers stay RhinoApp-free and logging is the runner's err
    // channel only, never an assertion surface.
    internal static class RhinoApp
    {
        public static void WriteLine(string text) { }

        // ConversationStore.Save marshals its PersistentSettings write onto the UI thread. Headless
        // there is no UI thread, so run the action inline: deterministic and keeps Save synchronous
        // for the store tests.
        public static void InvokeOnUiThread(Delegate method, params object[] args) =>
            method.DynamicInvoke(args);
    }
}

namespace Rhino.PlugIns
{
    // The slice of PersistentSettings ConversationStore touches: a flat string keystore plus the
    // child-node accessors AISettings.Conversations relies on. In-memory, deterministic, resettable.
    internal sealed class PersistentSettings
    {
        private Dictionary<string, string> Strings { get; } = new();

        public void SetString(string key, string value) => Strings[key] = value;

        public bool TryGetString(string key, out string value) => Strings.TryGetValue(key, out value!);

        public void DeleteItem(string key) => Strings.Remove(key);

        public IEnumerable<string> Keys => new List<string>(Strings.Keys);
    }
}

namespace RhMcp
{
    // The AISettings members the compiled-in closure reads. Tests assign these directly to script
    // settings-driven behaviour (ResolveMcpServers, registry resolution) and to reset the transcript
    // store. GetAgents/DefaultAgentName feed AgentRegistry; the agent chain defaults to the real
    // built-ins so registry resolution sees out-of-the-box ordering unless a test overrides it.
    internal static class AISettings
    {
        public static string ExtraMcpServersJson { get; set; } = "{\"mcpServers\":{}}";

        public static Rhino.PlugIns.PersistentSettings Conversations { get; set; } = new();

        public static string DefaultAgentName { get; set; } = "claude";

        // When set (resolution tests), GetAgents returns this chain verbatim. When null, GetAgents
        // mirrors production: built-ins overlaid with CustomAgents through the SAME pure
        // AgentRegistry.Overlay, so the overlay/dedup invariant the tests assert can't drift from
        // the real AISettings.GetAgents.
        private static IReadOnlyList<AgentDefinition>? ChainOverride { get; set; }

        private static IReadOnlyList<AgentDefinition> CustomAgents { get; set; } = [];

        public static IReadOnlyList<AgentDefinition> GetAgents() =>
            ChainOverride ?? AgentRegistry.Overlay(AgentRegistry.Builtins(), CustomAgents);

        public static void SetAgentsForTest(IReadOnlyList<AgentDefinition> agents) => ChainOverride = agents;

        public static void SetCustomAgentsForTest(IReadOnlyList<AgentDefinition> custom)
        {
            ChainOverride = null;
            CustomAgents = custom;
        }

        public static void ResetForTest()
        {
            ExtraMcpServersJson = "{\"mcpServers\":{}}";
            Conversations = new();
            DefaultAgentName = "claude";
            ChainOverride = null;
            CustomAgents = [];
        }
    }

    // AgentPrompts.Compose only needs the server-name const; the real RouterMcpConfig also resolves
    // an on-disk router path through RhMcpPlugin (RhinoCommon), which is out of scope for headless
    // parser tests. Shimming just the const keeps AgentPrompts pure.
    internal static class RouterMcpConfig
    {
        internal const string ServerName = "rhino";
    }
}
