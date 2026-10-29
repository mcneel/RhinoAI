using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

using Acp;
using ContentBlock = Acp.ContentBlock;

using Rhino.AI.Server;
using Rhino.Runtime;

namespace Rhino.AI;

// Establishes the transport bridge, MCP configuration, and Rhino 3D system-prompt
// priming for Gemini / Antigravity (agy).
internal static class GeminiConnection
{
    // Explicit system directive anchoring Gemini inside Rhino 3D so it immediately and
    // exclusively invokes registered Rhino MCP tools via call_mcp_tool on turn 1 without
    // conversational preamble, schema file reading, or filesystem/web exploration.
    internal const string RhinoSystemDirective =
        "You are an AI assistant embedded directly inside Rhino 3D via the RhinoAI plugin.\n"
        + "CRITICAL EXECUTION RULES (MUST FOLLOW WITHOUT EXCEPTION):\n"
        + "1. ONLY USE RHINO MCP TOOLS (PLUS `image-generator` FOR IMAGES): For all Rhino 3D and Grasshopper tasks, exclusively call `call_mcp_tool` with `\"ServerName\": \"rhino\"`. "
        + "For generating, creating, or rendering images (Nano Banana 2 / Nano Banana Pro), call `invoke_subagent` with `\"TypeName\": \"image-generator\"` (always supplying the required Antigravity properties `\"toolAction\": \"Rendering viewport image\"` and `\"toolSummary\": \"Render viewport\"`). "
        + "NEVER call `run_command`, `search_web`, `read_url_content`, `write_to_file`, `replace_file_content`, `manage_task`, `view_file`, `schedule`, `manage_subagents`, or `ask_question` (`view_file` is reserved for the image-generator subagent; never call it from the main session).\n"
        + "2. DO NOT READ SCHEMA FILES OR TRANSCRIPTS FROM DISK: All `rhino` MCP tool schemas are already provided inline below. "
        + "NEVER call `view_file` or `run_command` to inspect `~/.gemini/antigravity-cli/mcp/rhino/*.json`, `instructions.md`, `brain/` transcripts, or any subagent logs.\n"
        + "3. ACT IMMEDIATELY IN ONE `run_csharp` CALL WITH MINIMAL THINKING: Keep internal reasoning brief. "
        + "When given a geometry or layer creation task, skip `get_context` and `zoom_to_*` unless asked, and execute in a single `run_csharp` call using the canonical RhinoCommon pattern below. "
        + "UNITS AND SIZING: RhinoCommon coordinates operate in active document units (`doc.ModelUnitSystem`). "
        + "When dimensions are unspecified by the user (e.g. \"create a sphere\"), choose natural dimensions for the active document unit system (e.g. radius ~ 1–3 units if Feet or Meters; 25–100 units if Millimeters; 2–10 units if Inches). "
        + "Never assume Millimeters when units are unspecified! Only use `RhinoMath.UnitScale(sourceUnit, doc.ModelUnitSystem)` when the user explicitly requests dimensions in a specific unit (e.g. \"40mm circle\" -> radius = 20.0 * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem)). "
        + "Always use `run_csharp` instead of `run_python` (unless the user explicitly asks for Python) because `run_csharp` runs natively in-process on .NET without spawning Python 3.\n"
        + "4. IMAGE GENERATION & VIEWPORT RENDERING (NANO BANANA 2): When asked to create, generate, or render an image, invoke the built-in `image-generator` subagent (`invoke_subagent` with `\"TypeName\": \"image-generator\"`, `\"toolAction\": \"Rendering viewport image\"`, `\"toolSummary\": \"Render viewport\"`). "
        + "If rendering or restyling the active Rhino viewport, first call `get_viewport_image` (`Arguments: { \"width\": 1280, \"height\": 720 }`), which saves the viewport snapshot to disk and returns its file path in `metadata.viewport.path`, and pass that file path in the prompt to `image-generator`. "
        + "Immediately after invoking `image-generator`, stop calling tools (never call `schedule`, `manage_subagents`, or polling loops) and do not write waiting notes. The system will automatically notify you when the subagent completes. "
        + "Once notified with the completed image, output the markdown link `[Rendered Image](file:///...)` to the saved image path followed by the **Scene Highlights** bulleted summary so RhinoAI displays the rendering cleanly in the chat panel without redundant clutter.\n\n"
        + "CANONICAL `run_csharp` PATTERN (use directly without deliberating over RhinoCommon signatures):\n"
        + "```csharp\n"
        + "using Rhino;\nusing Rhino.Geometry;\nusing Rhino.DocObjects;\nusing System.Drawing;\n"
        + "var doc = __rhino_doc__;\n"
        + "int idx = doc.Layers.FindByFullPath(\"LayerName\", -1);\n"
        + "if (idx < 0) idx = doc.Layers.Add(new Layer { Name = \"LayerName\", Color = Color.White });\n"
        + "else { var l = doc.Layers[idx]; l.Color = Color.White; doc.Layers.Modify(l, idx, true); }\n"
        + "Guid id = doc.Objects.AddCircle(new Circle(Plane.WorldXY, Point3d.Origin, 5.0), new ObjectAttributes { LayerIndex = idx });\n"
        + "doc.Views.Redraw();\n"
        + "Console.WriteLine($\"Created {id} on layer index {idx}\");\n"
        + "```\n\n"
        + "RHINO MCP TOOLS REFERENCE (invoke via `call_mcp_tool` with `\"ServerName\": \"rhino\"`, `\"ToolName\": \"<name>\"`, `\"Arguments\": { ... }`):\n"
        + "- `run_csharp`: `Arguments: { \"script\": \"<c# code>\" }` (required: `script`) -> PREFERRED for all geometry/layer scripting. Uses `__rhino_doc__` (`RhinoDoc`) and `Console.WriteLine(...)`.\n"
        + "- `get_context`: `Arguments: {}` -> Snapshot of selection, activeViewport, document (objectCount, layerCount), and Grasshopper status. Skip when purely creating new geometry.\n"
        + "- `run_command`: `Arguments: { \"command\": \"<Rhino command macro>\" }` (required: `command`) -> Run a Rhino command macro, e.g. `\"_Circle 0,0,0 _Diameter 10\"`.\n"
        + "- `get_commands`: `Arguments: { \"filter\": \"<substring>\" }` -> Discover Rhino command names matching filter.\n"
        + "- `list_objects`: `Arguments: { \"layer\": \"<path>\", \"geometryType\": \"point|pointset|curve|surface|brep|mesh|annotation|light|block\", \"names\": [\"...\"], \"includeHidden\": false, \"includeLocked\": true, \"limit\": 1000 }` -> Query document objects.\n"
        + "- `get_selection`: `Arguments: {}` -> Get currently selected objects.\n"
        + "- `set_selection`: `Arguments: { \"ids\": [\"<guid>\"], \"names\": [\"...\"], \"layer\": \"<path>\", \"geometryType\": \"...\" }` -> Select objects matching filter.\n"
        + "- `set_layer_material`: `Arguments: { \"layer\": \"<path>\", \"color\": \"#RRGGBB\", \"applyToLayerColor\": true, \"transparency\": 0.0, \"gloss\": 0.0 }` (required: `layer`) -> Set layer render material and display color.\n"
        + "- `zoom_to_layer`: `Arguments: { \"layer\": \"<path>\" }` (required: `layer`) -> Zoom active viewport to fit objects on layer.\n"
        + "- `zoom_to_object`: `Arguments: { \"ids\": [\"<guid>\"] }` (required: `ids`) -> Zoom active viewport to fit object GUIDs.\n"
        + "- `set_camera`: `Arguments: { \"location\": {\"x\":0,\"y\":0,\"z\":0}, \"target\": {\"x\":0,\"y\":0,\"z\":0}, \"up\": {\"x\":0,\"y\":0,\"z\":1}, \"lensLength\": 50, \"projection\": \"perspective|parallel\", \"boxMin\": {\"x\":0,\"y\":0,\"z\":0}, \"boxMax\": {\"x\":0,\"y\":0,\"z\":0} }` -> Set active viewport camera.\n"
        + "- `get_viewport_image`: `Arguments: { \"viewport\": \"<name>\", \"width\": 480, \"height\": 270 }` -> Capture viewport image.\n"
        + "- `open_doc`: `Arguments: { \"path\": \"<abs path>\", \"clearFirst\": false }` (required: `path`).\n"
        + "- `save_doc`: `Arguments: { \"path\": \"<abs path>\" }` (required: `path`).\n"
        + "- `close_doc`: `Arguments: { \"save\": false }`.\n"
        + "- `ask_user`: `Arguments: { \"questions\": [{ \"question\": \"...\", \"options\": [\"...\", \"...\"], \"multiSelect\": false }] }` (required: `questions`).\n"
        + "- `run_python`: `Arguments: { \"script\": \"<python3 code>\" }` (required: `script`) -> Only use if the user explicitly requests Python; otherwise always use `run_csharp` to avoid cold-starting Python 3.\n"
        + "- Grasshopper (GH2 / GH1): `g2_start`, `g2_get_canvas_graph`, `g2_search_components` (`{\"query\":\"...\"}`), `g2_describe_component` (`{\"selector\":\"...\"}`), `g2_apply_graph` (`{\"components\":[...],\"sliders\":[...],\"wires\":[...],\"deleteIds\":[...],\"solve\":true}`), `g2_solve_canvas`, `g2_clear_canvas` (and corresponding `g1_*` tools).";

    private static string WorkspaceStash =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RhinoAI", "Gemini");

    public static string BuildSystemPrompt(AgentDefinition def)
    {
        string userOrDefaultPrompt;
        try
        {
            userOrDefaultPrompt = AISettings.EffectivePrompt(def);
        }
        catch
        {
            userOrDefaultPrompt = def.DefaultPrompt ?? string.Empty;
        }

        string anchored = string.IsNullOrWhiteSpace(userOrDefaultPrompt)
            ? RhinoSystemDirective
            : userOrDefaultPrompt.Contains("RHINO MCP TOOLS REFERENCE", StringComparison.OrdinalIgnoreCase)
                ? userOrDefaultPrompt
                : RhinoSystemDirective + "\n\n" + userOrDefaultPrompt;

        return AgentPrompts.Compose(anchored);
    }

    public static string FormatPrimedContent(string userText, string? systemPrelude)
    {
        string prelude = string.IsNullOrWhiteSpace(systemPrelude)
            ? AgentPrompts.Compose(RhinoSystemDirective)
            : systemPrelude!;

        if (string.IsNullOrWhiteSpace(userText))
            return $"<system_instruction>\n{prelude}\n</system_instruction>";

        if (userText.Contains("<system_instruction>", StringComparison.Ordinal))
            return userText;

        return $"<system_instruction>\n{prelude}\n</system_instruction>\n\n{userText}";
    }

    public static string BuildTurnPayload(AgentDefinition def, string? systemPrelude, IReadOnlyList<ContentBlock> prompt, bool resume = false)
    {
        StringBuilder builder = new();
        List<string> attachedImages = new();

        foreach (ContentBlock block in prompt)
        {
            if (block is TextContentBlock text && text.Text.Length > 0)
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(text.Text);
            }
            else if (block is ImageContentBlock image && !string.IsNullOrWhiteSpace(image.Data))
            {
                try
                {
                    string workspace = WorkspaceStash;
                    Directory.CreateDirectory(workspace);
                    string ext = ExtensionFor(image.MimeType);
                    string fileName = $"attached_reference_{Guid.NewGuid():N}{ext}";
                    string imagePath = Path.Combine(workspace, fileName);
                    File.WriteAllBytes(imagePath, Convert.FromBase64String(image.Data));
                    attachedImages.Add(imagePath);
                }
                catch (Exception ex)
                {
                    HostUtils.LogDebugEvent($"[gemini:spill] could not spill attached image: {ex.Message}.\n");
                }
            }
        }

        if (attachedImages.Count > 0)
        {
            if (builder.Length > 0) builder.Append("\n\n");
            builder.Append("Attached Reference / Style Reference Images:\n");
            foreach (string path in attachedImages)
            {
                string forwardSlashPath = path.Replace('\\', '/');
                builder.Append($"- [Attached Reference Image](file:///{forwardSlashPath})\n");
            }
            builder.Append("When generating or rendering images, match the aesthetic style, color palette, lighting, and mood from the attached reference image above.");
        }

        string resolvedPrelude = string.IsNullOrWhiteSpace(systemPrelude)
            ? BuildSystemPrompt(def)
            : systemPrelude!;
        string userText = builder.ToString();
        string primedContent = resume ? userText : FormatPrimedContent(userText, resolvedPrelude);

        JsonObject payload = new()
        {
            ["event"] = "user",
            ["system_instruction"] = resolvedPrelude,
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = primedContent,
            },
        };

        return payload.ToJsonString(McpSerializer.Options);
    }

    private static string ExtensionFor(string? mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".png",
    };

    // Prepares an isolated workspace directory (%APPDATA%/RhinoAI/Gemini) containing AGENTS.md and
    // .agents/hooks.json so agy loads the Rhino rules as top-priority <user_rules> and hard-blocks
    // built-in filesystem/shell/web tools via PreToolUse (matching Claude's --tools "" and CodexHome).
    // invoke_subagent and view_file stay off the deny list so agy can delegate image generation to its
    // built-in image-generator subagent and inspect the viewport capture.
    public static string PrepareWorkspace(string? systemPrelude = null)
    {
        string workspace = WorkspaceStash;
        try
        {
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(Path.Combine(workspace, ".git"));

            string rulesText = string.IsNullOrWhiteSpace(systemPrelude)
                ? AgentPrompts.Compose(RhinoSystemDirective)
                : systemPrelude!;
            File.WriteAllText(Path.Combine(workspace, "AGENTS.md"), rulesText);
            File.WriteAllText(Path.Combine(workspace, "GEMINI.md"), rulesText);

            string agentsDir = Path.Combine(workspace, ".agents");
            Directory.CreateDirectory(agentsDir);

            // Write static deny.json payload to avoid shell quote mangling under cmd.exe on Windows.
            JsonObject denyObj = new()
            {
                ["decision"] = "deny",
                ["reason"] = "Built-in filesystem, shell, and web tools are disabled in RhinoAI. Do not read .json schema files or run shell commands. Call call_mcp_tool with ServerName='rhino' directly using the schemas in your system instructions.",
            };
            File.WriteAllText(Path.Combine(agentsDir, "deny.json"), denyObj.ToJsonString(McpSerializer.Options));

            string denyCommand = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "type deny.json"
                : "cat deny.json";

            JsonObject hooksJson = new()
            {
                ["rhino-mcp-only"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["PreToolUse"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["matcher"] = "run_command|write_to_file|replace_file_content|search_web|read_url_content|manage_task|define_subagent|manage_subagents|ask_question|schedule",
                            ["hooks"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["type"] = "command",
                                    ["command"] = denyCommand,
                                    ["timeout"] = 5,
                                },
                            },
                        },
                    },
                },
            };

            File.WriteAllText(Path.Combine(agentsDir, "hooks.json"), hooksJson.ToJsonString(McpSerializer.Options));
        }
        catch (Exception ex)
        {
            HostUtils.LogDebugEvent($"[gemini:workspace] could not prepare workspace: {ex.Message}.\n");
        }

        return workspace;
    }

    // Ensures Antigravity's ~/.gemini/config/mcp_config.json registers the active Rhino HTTP MCP
    // endpoint and seeds instructions.md in the MCP cache so agy invokes Rhino tools on turn 1.
    public static void EnsureMcpConfigured(string mcpUrl, IReadOnlyList<string> extraServers, string? systemPrelude = null)
    {
        if (string.IsNullOrWhiteSpace(mcpUrl))
            return;

        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                return;

            string configDir = Path.Combine(home, ".gemini", "config");
            Directory.CreateDirectory(configDir);
            string configPath = Path.Combine(configDir, "mcp_config.json");

            JsonObject root = new();
            if (File.Exists(configPath))
            {
                string existing = File.ReadAllText(configPath);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    try
                    {
                        if (JsonNode.Parse(existing) is JsonObject parsed)
                            root = parsed;
                    }
                    catch
                    {
                        root = new JsonObject();
                    }
                }
            }

            if (root["mcpServers"] is not JsonObject servers)
            {
                servers = new JsonObject();
                root["mcpServers"] = servers;
            }

            servers[RouterMcpConfig.ServerName] = new JsonObject
            {
                ["url"] = mcpUrl,
            };

            foreach (string entry in extraServers)
            {
                try
                {
                    if (JsonNode.Parse(entry) is JsonObject extraObj)
                    {
                        foreach (KeyValuePair<string, JsonNode?> kvp in extraObj)
                        {
                            if (kvp.Key != RouterMcpConfig.ServerName && kvp.Value is JsonNode node)
                                servers[kvp.Key] = node.DeepClone();
                        }
                    }
                }
                catch
                {
                }
            }

            File.WriteAllText(configPath, root.ToJsonString(McpSerializer.Options));

            string instructions = string.IsNullOrWhiteSpace(systemPrelude)
                ? AgentPrompts.Compose(RhinoSystemDirective)
                : systemPrelude!;

            foreach (string appFolder in new[] { "antigravity-cli", "antigravity" })
            {
                try
                {
                    string rhinoMcpDir = Path.Combine(home, ".gemini", appFolder, "mcp", RouterMcpConfig.ServerName);
                    Directory.CreateDirectory(rhinoMcpDir);
                    File.WriteAllText(Path.Combine(rhinoMcpDir, "instructions.md"), instructions);

                    foreach (string staleRouterFile in Directory.GetFiles(rhinoMcpDir, "_router_*.json"))
                    {
                        try { File.Delete(staleRouterFile); } catch { }
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            HostUtils.LogDebugEvent($"[gemini:mcp] could not write mcp_config.json: {ex.Message}.\n");
        }
    }

    public static IAcpAgent CreateAgent(AgentDefinition def, IAcpClient client, Conversation conversation, string cwd, Guid? resumeSessionId = null)
    {
        string systemPrelude = BuildSystemPrompt(def);
        string workspace = PrepareWorkspace(systemPrelude);
        return new StreamJsonAgent(def, client, conversation, workspace, new AntigravityStreamJsonParser(def, systemPrelude), resumeSessionId);
    }

    public static IAcpAgent Connect(AgentDefinition def, IAcpClient client, string cwd)
    {
        if (!CliProcess.TryResolve(def.SearchPaths.GetPaths(), out string path))
            throw new FileNotFoundException("Neither Antigravity (agy.exe) nor Gemini CLI was found on your system.");

        string systemPrompt = BuildSystemPrompt(def);
        string workspace = PrepareWorkspace(systemPrompt);

        ProcessStartInfo psi = new()
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workspace,
        };
        CliProcess.ConfigureEncoding(psi);
        CliProcess.ConfigureFileName(psi, path);

        psi.Environment["GEMINI_SYSTEM_PROMPT"] = systemPrompt;
        psi.Environment["ENABLE_TOOL_SEARCH"] = "false";

        string fileName = Path.GetFileName(path).ToLowerInvariant();
        if (fileName.Contains("agy"))
        {
            psi.AddArgument("--input-format");
            psi.AddArgument("stream-json");
            psi.AddArgument("--output-format");
            psi.AddArgument("stream-json");
            psi.AddArgument("--dangerously-skip-permissions");
            psi.AddArgument("--disable-slash-commands");
        }
        else
        {
            psi.AddArgument("--experimental-acp");
        }

        Process proc = new() { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                HostUtils.LogDebugEvent($"[{def.Name}:err] {e.Data}.\n");
        };
        proc.Start();
        proc.BeginErrorReadLine();

        try
        {
            ProcessStdioTransport transport = new(proc);
            ClientSideConnection connection = new(_ => client, transport);
            connection.Start();
            return connection;
        }
        catch
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process already gone
            }
            proc.Dispose();
            throw;
        }
    }
}