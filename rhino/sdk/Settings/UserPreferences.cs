using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Rhino.AI;

/// <summary>
/// Global User Preferences for AI Models, Agents
/// </summary>
public class UserPreferences
{

    internal static Guid DefaultAgentId { get; } = new("d07df3c4-2c1d-40a1-a90c-7e85f1acae62");

    public Guid PreferedAgentId { get; internal set; } = DefaultAgentId;

    public int ZoomLevel { get; internal set; }
    public bool AutoLoadMCP { get; internal set; }
    public bool AutoLoadScriptPlugIn { get; internal set; } = true;
    public string? ScriptPluginName { get; internal set; }

    /// <summary>
    /// Global user Permissions
    /// </summary>
    public PermissionSet Permissions { get; internal set; } = new();

    public Permissability DefaultPermission { get; internal set; } = Permissability.Ask;

    internal List<IMcpDefinition> CustomMcps { get; } = [];

    public UserPreferences()
    {
        InitDefaults();
        // Json deserialization will override this, which is correct!
    }

    // TODO : These must ABSOLUTELY load from somewhere

    // NOTE : I think the below is unnecessary

    private const string RhinoMcpName = "Rhino";

    private static (string Tool, string[] Args)[] RhinoTools { get; } =
    [
        ("ask_user", ["questions"]),

        ("close_doc", ["path", "slot"]),
        ("get_commands", ["filter", "slot"]),
        ("get_context", ["slot"]),
        ("get_selection", ["slot"]),
        ("get_viewport_image", ["view", "displayMode", "width", "height", "cameraLocation", "target", "boxMin", "boxMax", "zoom", "slot"]),
        ("list_objects", ["names", "layer", "geometryType", "includeHidden", "includeLocked", "limit", "slot"]),
        ("manage_plugin_commands", ["action", "commandName", "script", "svg", "slot"]),
        ("open_doc", ["path", "clearFirst", "slot"]),
        ("run_command", ["command", "slot"]),
        ("run_csharp", ["script", "slot"]),
        ("run_python", ["script", "slot"]),
        ("save_doc", ["path", "slot"]),
        ("set_camera", ["location", "target", "up", "lensLength", "projection", "boxMin", "boxMax", "slot"]),
        ("set_layer_material", ["layer", "color", "transparency", "gloss", "applyToLayerColor", "slot"]),
        ("set_selection", ["ids", "names", "layer", "geometryType", "slot"]),
        ("zoom_to_layer", ["layer", "slot"]),
        ("zoom_to_object", ["ids", "slot"]),

        ("g1_apply_graph", ["sliders", "components", "wires", "replace", "includeDeprecated", "solve", "slot"]),
        ("g1_clear_canvas", ["confirm", "solve", "slot"]),
        ("g1_connect", ["src_id", "src", "dst_id", "dst", "replace", "solve", "slot"]),
        ("g1_connect_many", ["wires", "replace", "solve", "slot"]),
        ("g1_delete_component", ["selector", "solve", "slot"]),
        ("g1_describe_component", ["name", "includeDeprecated", "slot"]),
        ("g1_get_canvas_graph", ["include_data", "sample_size", "slot"]),
        ("g1_place_component", ["selector", "x", "y", "includeDeprecated", "solve", "slot"]),
        ("g1_place_slider", ["min", "value", "max", "type", "name", "x", "y", "solve", "slot"]),
        ("g1_search_components", ["query", "category", "subcategory", "limit", "includeDeprecated", "slot"]),
        ("g1_solve_graph", ["zoom_views", "slot"]),
        ("g1_start", ["slot"]),

        ("g2_apply_graph", ["sliders", "components", "wires", "replace", "includeDeprecated", "solve", "slot"]),
        ("g2_clear_canvas", ["confirm", "solve", "slot"]),
        ("g2_connect", ["src_id", "src", "dst_id", "dst", "replace", "solve", "slot"]),
        ("g2_connect_many", ["wires", "replace", "solve", "slot"]),
        ("g2_delete_component", ["selector", "solve", "slot"]),
        ("g2_describe_component", ["name", "includeDeprecated", "slot"]),
        ("g2_get_canvas_graph", ["include_data", "sample_size", "slot"]),
        ("g2_place_component", ["selector", "x", "y", "includeDeprecated", "solve", "slot"]),
        ("g2_place_slider", ["min", "value", "max", "decimals", "name", "x", "y", "solve", "slot"]),
        ("g2_search_components", ["query", "category", "subcategory", "limit", "includeDeprecated", "slot"]),
        ("g2_solve_canvas", ["slot"]),
        ("g2_start", ["slot"]),
    ];

    public void InitDefaults()
    {
        foreach ((string tool, string[] args) in RhinoTools)
        {
            Dictionary<string, ToolArgPermission> argPermissions = new(args.Length);
            foreach (string arg in args)
                argPermissions[arg] = new ToolArgPermission(arg, ArgRule.Anything);

            Permissions.AddPermission(new Permission(RhinoMcpName, tool, Permissability.Always, argPermissions));
        }
    }

}

[JsonDerivedType(typeof(HttpMcpDefinition), "http")]
[JsonDerivedType(typeof(StdioMcpDefinition), "stdio")]
internal interface IMcpDefinition
{
    public string Name { get; }
    public bool Enabled { get; }   
}

internal sealed record HttpMcpDefinition(string Name, bool Enabled, Uri Url, IReadOnlyDictionary<string, string> Headers) : IMcpDefinition;

internal sealed record StdioMcpDefinition(string Name, bool Enabled, string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env) : IMcpDefinition;

