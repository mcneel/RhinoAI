using System.Text;
using System.Text.Json;

// Acp.Codegen: reads the pinned ACP schema.json + meta.json and emits committed C# under
// src/Generated/. Run from the repo root: `dotnet run --project rhino/acp/codegen`.
// Output is deterministic (defs iterated in sorted order) so a clean regen produces no diff.

// Committed Generated/*.g.cs are pure LF; force LF on write so a Windows regen (where
// StringBuilder.AppendLine emits Environment.NewLine = CRLF) does not produce a spurious diff.
static void WriteGenerated(string path, string content) =>
    File.WriteAllText(path, content.Replace("\r\n", "\n"));

string root = args.Length > 0 ? args[0] : "rhino/acp/schema/schema.json";
string metaPath = args.Length > 1 ? args[1] : "rhino/acp/schema/meta.json";
string outDir = args.Length > 2 ? args[2] : "rhino/acp/src/Generated";
Directory.CreateDirectory(outDir);

using JsonDocument schemaDoc = JsonDocument.Parse(File.ReadAllText(root));
using JsonDocument metaDoc = JsonDocument.Parse(File.ReadAllText(metaPath));
JsonElement defs = schemaDoc.RootElement.GetProperty("$defs");

// Primitive aliases collapse to their underlying C# type at every use site (no wrapper structs).
Dictionary<string, string> primAlias = new()
{
    ["PermissionOptionId"] = "string",
    ["SessionId"] = "string",
    ["SessionModeId"] = "string",
    ["ToolCallId"] = "string",
    ["ProtocolVersion"] = "int",
};
// Free-form / open schemas surface as raw JSON.
HashSet<string> free = new() { "ExtRequest", "ExtResponse", "ExtNotification" };

bool Skip(string name, JsonElement s) =>
    primAlias.ContainsKey(name) || free.Contains(name) || name == "RequestId"
    || (s.TryGetProperty("x-docs-ignore", out JsonElement ig) && ig.ValueKind == JsonValueKind.True);

// ---- helpers -------------------------------------------------------------------------------

static string Pascal(string s)
{
    StringBuilder sb = new();
    bool upper = true;
    foreach (char c in s)
    {
        if (c is '_' or '/' or '-' or '.' or ' ') { upper = true; continue; }
        sb.Append(upper ? char.ToUpperInvariant(c) : c);
        upper = false;
    }
    return sb.ToString();
}

static string Summary(JsonElement s)
{
    if (!s.TryGetProperty("description", out JsonElement d) || d.ValueKind != JsonValueKind.String)
        return string.Empty;
    string text = d.GetString()!;
    int cut = text.IndexOf('\n');
    if (cut >= 0) text = text[..cut];
    text = text.Trim();
    if (text.Length > 200) text = text[..200];
    return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

static void Doc(StringBuilder sb, string indent, JsonElement s)
{
    string sum = Summary(s);
    if (sum.Length > 0) sb.AppendLine($"{indent}/// <summary>{sum}</summary>");
}

string IntType(JsonElement s) =>
    s.TryGetProperty("format", out JsonElement f) && f.ValueKind == JsonValueKind.String
        ? f.GetString() switch { "int64" or "uint32" => "long", "uint64" => "ulong", _ => "int" }
        : "int";

string MapRef(string name) =>
    primAlias.TryGetValue(name, out string? p) ? p
    : free.Contains(name) ? "JsonElement"
    : name == "RequestId" ? "RequestId"
    : name;

// Resolves a property schema to its non-nullable C# type.
string CsCore(JsonElement s)
{
    if (s.TryGetProperty("$ref", out JsonElement r))
        return MapRef(r.GetString()!.Split('/')[^1]);

    if (s.TryGetProperty("allOf", out JsonElement allOf) && allOf.GetArrayLength() >= 1)
    {
        JsonElement first = allOf[0];
        if (first.TryGetProperty("$ref", out JsonElement ar))
            return MapRef(ar.GetString()!.Split('/')[^1]);
        return "JsonElement";
    }

    if (s.TryGetProperty("anyOf", out JsonElement anyOf))
    {
        List<JsonElement> nonNull = new();
        foreach (JsonElement b in anyOf.EnumerateArray())
            if (!(b.TryGetProperty("type", out JsonElement bt) && bt.ValueKind == JsonValueKind.String && bt.GetString() == "null"))
                nonNull.Add(b);
        return nonNull.Count == 1 ? CsCore(nonNull[0]) : "JsonElement";
    }

    if (!s.TryGetProperty("type", out JsonElement t))
        return "JsonElement";

    string? type;
    if (t.ValueKind == JsonValueKind.Array)
    {
        List<string> kinds = t.EnumerateArray().Select(e => e.GetString()!).Where(x => x != "null").ToList();
        type = kinds.Count == 1 ? kinds[0] : null;
    }
    else
    {
        type = t.ValueKind == JsonValueKind.String ? t.GetString() : null;
    }

    return type switch
    {
        "array" => CsCore(s.GetProperty("items")) + "[]",
        "integer" => IntType(s),
        "number" => "double",
        "boolean" => "bool",
        "string" => "string",
        _ => "JsonElement",
    };
}

string CsType(JsonElement s, bool required)
{
    string core = CsCore(s);
    return required ? core : core.EndsWith("?") ? core : core + "?";
}

// Emits the properties of an object schema as record members. `discProp` is excluded (handled by
// the union base). `forceOptional` relaxes `required` (used when merging anyOf branches).
void EmitProps(StringBuilder sb, string ownerType, JsonElement s, string? discProp = null, bool forceOptional = false)
{
    if (!s.TryGetProperty("properties", out JsonElement props)) return;
    HashSet<string> required = new();
    if (!forceOptional && s.TryGetProperty("required", out JsonElement req))
        foreach (JsonElement x in req.EnumerateArray()) required.Add(x.GetString()!);

    HashSet<string> emitted = new(StringComparer.Ordinal);
    foreach (JsonProperty p in props.EnumerateObject())
    {
        if (p.Name == discProp) continue;
        bool isReq = required.Contains(p.Name);
        string member = Pascal(p.Name);
        if (member == ownerType) member += "Value";
        if (!emitted.Add(member))
            throw new InvalidOperationException(
                $"{ownerType}: JSON property '{p.Name}' Pascal-folds to member '{member}', which already exists on this record.");
        Doc(sb, "    ", p.Value);
        sb.AppendLine($"    [JsonPropertyName(\"{p.Name}\")]");
        string reqKw = isReq ? "required " : string.Empty;
        sb.AppendLine($"    public {reqKw}{CsType(p.Value, isReq)} {member} {{ get; init; }}");
    }
}

string Header(string extra = "") =>
    "// <auto-generated>Generated by Acp.Codegen from schema/schema.json. Do not edit.</auto-generated>\n"
    + "#nullable enable\n"
    + "using System.Text.Json;\nusing System.Text.Json.Serialization;\n" + extra + "\nnamespace Acp;\n";

// ---- classify ------------------------------------------------------------------------------

static bool IsConstEnum(JsonElement s) =>
    s.TryGetProperty("oneOf", out JsonElement oo) && oo.GetArrayLength() > 0
    && oo.EnumerateArray().All(b => b.TryGetProperty("const", out _));

static bool IsPlainEnum(JsonElement s) =>
    s.TryGetProperty("enum", out _) && s.TryGetProperty("type", out JsonElement t)
    && t.ValueKind == JsonValueKind.String && t.GetString() == "string";

List<string> names = defs.EnumerateObject().Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

// ---- Enums.g.cs ----------------------------------------------------------------------------
{
    StringBuilder sb = new(Header());
    foreach (string name in names)
    {
        JsonElement s = defs.GetProperty(name);
        if (Skip(name, s) || !(IsPlainEnum(s) || IsConstEnum(s))) continue;

        List<string> values = IsPlainEnum(s)
            ? s.GetProperty("enum").EnumerateArray().Select(v => v.GetString()!).ToList()
            : s.GetProperty("oneOf").EnumerateArray().Select(v => v.GetProperty("const").GetString()!).ToList();

        sb.AppendLine();
        Doc(sb, string.Empty, s);
        sb.AppendLine($"[JsonConverter(typeof({name}JsonConverter))]");
        sb.AppendLine($"public enum {name}");
        sb.AppendLine("{");
        foreach (string v in values) sb.AppendLine($"    {Pascal(v)},");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"internal sealed class {name}JsonConverter : JsonConverter<{name}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public override {name} Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options) => reader.GetString() switch");
        sb.AppendLine("    {");
        foreach (string v in values) sb.AppendLine($"        \"{v}\" => {name}.{Pascal(v)},");
        sb.AppendLine($"        var other => throw new JsonException($\"Unknown {name}: {{other}}\"),");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine($"    public override void Write(Utf8JsonWriter writer, {name} value, JsonSerializerOptions options) => writer.WriteStringValue(value switch");
        sb.AppendLine("    {");
        foreach (string v in values) sb.AppendLine($"        {name}.{Pascal(v)} => \"{v}\",");
        sb.AppendLine($"        _ => throw new JsonException($\"Unknown {name}: {{value}}\"),");
        sb.AppendLine("    });");
        sb.AppendLine("}");
    }
    WriteGenerated(Path.Combine(outDir, "Enums.g.cs"), sb.ToString());
}

// ---- Unions.g.cs (4 discriminated + McpServer) ---------------------------------------------
void EmitUnion(StringBuilder sb, string baseName, string discProp, IEnumerable<(string Tag, string Variant, JsonElement Payload, JsonElement Branch)> variants)
{
    string discMember = Pascal(discProp);
    if (discMember == baseName) discMember += "Tag"; // a member can't share its type's name
    sb.AppendLine();
    sb.AppendLine($"[JsonConverter(typeof({baseName}JsonConverter))]");
    sb.AppendLine($"public abstract record {baseName}");
    sb.AppendLine("{");
    sb.AppendLine($"    [JsonPropertyName(\"{discProp}\")]");
    sb.AppendLine($"    public abstract string {discMember} {{ get; }}");
    sb.AppendLine("}");

    List<(string Tag, string Variant)> cases = new();
    foreach ((string tag, string variant, JsonElement payload, JsonElement branch) in variants)
    {
        cases.Add((tag, variant));
        sb.AppendLine();
        Doc(sb, string.Empty, branch);
        sb.AppendLine($"public sealed record {variant} : {baseName}");
        sb.AppendLine("{");
        sb.AppendLine($"    [JsonPropertyName(\"{discProp}\")]");
        sb.AppendLine($"    public override string {discMember} => \"{tag}\";");
        if (payload.ValueKind == JsonValueKind.Object) EmitProps(sb, variant, payload, discProp);
        sb.AppendLine("}");
    }

    sb.AppendLine();
    sb.AppendLine($"internal sealed class {baseName}JsonConverter : JsonConverter<{baseName}>");
    sb.AppendLine("{");
    sb.AppendLine($"    public override {baseName}? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)");
    sb.AppendLine("    {");
    sb.AppendLine("        using JsonDocument doc = JsonDocument.ParseValue(ref reader);");
    sb.AppendLine("        JsonElement root = doc.RootElement;");
    sb.AppendLine($"        string? disc = root.TryGetProperty(\"{discProp}\", out JsonElement d) ? d.GetString() : null;");
    sb.AppendLine("        return disc switch");
    sb.AppendLine("        {");
    foreach ((string tag, string variant) in cases)
        sb.AppendLine($"            \"{tag}\" => root.Deserialize<{variant}>(options)!,");
    sb.AppendLine($"            _ => throw new JsonException($\"Unknown {baseName} {discProp}: {{disc}}\"),");
    sb.AppendLine("        };");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine($"    public override void Write(Utf8JsonWriter writer, {baseName} value, JsonSerializerOptions options)");
    sb.AppendLine("        => JsonSerializer.Serialize(writer, value, value.GetType(), options);");
    sb.AppendLine("}");
}

JsonElement PayloadOf(JsonElement branch) =>
    branch.TryGetProperty("allOf", out JsonElement a) && a.GetArrayLength() >= 1 && a[0].TryGetProperty("$ref", out JsonElement r)
        ? defs.GetProperty(r.GetString()!.Split('/')[^1])
        : branch; // inline branch: its own properties are the payload

{
    StringBuilder sb = new(Header());
    foreach (string name in names)
    {
        JsonElement s = defs.GetProperty(name);
        if (Skip(name, s) || !s.TryGetProperty("discriminator", out JsonElement disc)) continue;
        string discProp = disc.GetProperty("propertyName").GetString()!;
        List<(string, string, JsonElement, JsonElement)> variants = new();
        foreach (JsonElement branch in s.GetProperty("oneOf").EnumerateArray())
        {
            string tag = branch.GetProperty("properties").GetProperty(discProp).GetProperty("const").GetString()!;
            variants.Add((tag, Pascal(tag) + name, PayloadOf(branch), branch));
        }
        EmitUnion(sb, name, discProp, variants);
    }

    // McpServer: discriminated by `type`, but the stdio branch omits the const (it is the default).
    if (defs.TryGetProperty("McpServer", out JsonElement mcp))
    {
        List<(string, string, JsonElement, JsonElement)> variants = new();
        foreach (JsonElement branch in mcp.GetProperty("anyOf").EnumerateArray())
        {
            string payloadRef = branch.GetProperty("allOf")[0].GetProperty("$ref").GetString()!.Split('/')[^1];
            string tag = branch.TryGetProperty("properties", out JsonElement bp) && bp.TryGetProperty("type", out JsonElement tp)
                ? tp.GetProperty("const").GetString()! : "stdio";
            string variant = payloadRef.Replace("McpServer", "") + "McpServer"; // McpServerHttp -> HttpMcpServer
            variants.Add((tag, variant, defs.GetProperty(payloadRef), branch));
        }
        // Stdio is the fallback when `type` is absent.
        sb.AppendLine();
        sb.AppendLine("// McpServer: `type` selects the transport; absent => stdio (the always-supported default).");
        EmitUnionWithDefault(sb, "McpServer", "type", variants, "stdio");
    }

    WriteGenerated(Path.Combine(outDir, "Unions.g.cs"), sb.ToString());
}

// Variant of EmitUnion whose converter tolerates a missing discriminator (falls back to defaultTag).
void EmitUnionWithDefault(StringBuilder sb, string baseName, string discProp, List<(string Tag, string Variant, JsonElement Payload, JsonElement Branch)> variants, string defaultTag)
{
    string discMember = Pascal(discProp);
    if (discMember == baseName) discMember += "Tag"; // a member can't share its type's name
    sb.AppendLine($"[JsonConverter(typeof({baseName}JsonConverter))]");
    sb.AppendLine($"public abstract record {baseName}");
    sb.AppendLine("{");
    sb.AppendLine($"    [JsonPropertyName(\"{discProp}\")]");
    sb.AppendLine($"    public abstract string {discMember} {{ get; }}");
    sb.AppendLine("}");
    foreach ((string tag, string variant, JsonElement payload, JsonElement branch) in variants)
    {
        sb.AppendLine();
        Doc(sb, string.Empty, branch);
        sb.AppendLine($"public sealed record {variant} : {baseName}");
        sb.AppendLine("{");
        sb.AppendLine($"    [JsonPropertyName(\"{discProp}\")]");
        sb.AppendLine($"    public override string {discMember} => \"{tag}\";");
        EmitProps(sb, variant, payload, discProp);
        sb.AppendLine("}");
    }
    sb.AppendLine();
    sb.AppendLine($"internal sealed class {baseName}JsonConverter : JsonConverter<{baseName}>");
    sb.AppendLine("{");
    sb.AppendLine($"    public override {baseName}? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)");
    sb.AppendLine("    {");
    sb.AppendLine("        using JsonDocument doc = JsonDocument.ParseValue(ref reader);");
    sb.AppendLine("        JsonElement root = doc.RootElement;");
    sb.AppendLine($"        string disc = root.TryGetProperty(\"{discProp}\", out JsonElement d) ? d.GetString()! : \"{defaultTag}\";");
    sb.AppendLine("        return disc switch");
    sb.AppendLine("        {");
    foreach ((string tag, string variant, _, _) in variants)
        sb.AppendLine($"            \"{tag}\" => root.Deserialize<{variant}>(options)!,");
    sb.AppendLine($"            _ => throw new JsonException($\"Unknown {baseName} {discProp}: {{disc}}\"),");
    sb.AppendLine("        };");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine($"    public override void Write(Utf8JsonWriter writer, {baseName} value, JsonSerializerOptions options)");
    sb.AppendLine("        => JsonSerializer.Serialize(writer, value, value.GetType(), options);");
    sb.AppendLine("}");
}

// ---- Types.g.cs (plain object records + merged/aliased anyOf) ------------------------------
{
    StringBuilder sb = new(Header());
    foreach (string name in names)
    {
        JsonElement s = defs.GetProperty(name);
        if (Skip(name, s) || IsPlainEnum(s) || IsConstEnum(s) || s.TryGetProperty("discriminator", out _) || name == "McpServer")
            continue;

        // Plain object.
        if (s.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String && t.GetString() == "object")
        {
            sb.AppendLine();
            Doc(sb, string.Empty, s);
            sb.AppendLine($"public sealed record {name}");
            sb.AppendLine("{");
            EmitProps(sb, name, s);
            sb.AppendLine("}");
            continue;
        }

        // anyOf without discriminator: single-branch alias (flatten) or multi-branch merge.
        if (s.TryGetProperty("anyOf", out JsonElement anyOf))
        {
            List<JsonElement> branches = anyOf.EnumerateArray()
                .Where(b => !(b.TryGetProperty("type", out JsonElement bt) && bt.ValueKind == JsonValueKind.String && bt.GetString() == "null"))
                .Select(PayloadOf).ToList();
            // Skip pure routing envelopes (id + result/error): handled by the JSON-RPC core.
            if (branches.All(b => b.TryGetProperty("properties", out JsonElement bp)
                    && bp.TryGetProperty("id", out _) && (bp.TryGetProperty("result", out _) || bp.TryGetProperty("error", out _))))
                continue;
            if (branches.Count == 0) continue;

            sb.AppendLine();
            Doc(sb, string.Empty, s);
            sb.AppendLine($"public sealed record {name}");
            sb.AppendLine("{");
            // Merge: emit each distinct property once; required only if required in every branch.
            HashSet<string> seen = new();
            HashSet<string> emitted = new(StringComparer.Ordinal);
            foreach (JsonElement b in branches)
            {
                if (!b.TryGetProperty("properties", out JsonElement props)) continue;
                foreach (JsonProperty p in props.EnumerateObject())
                {
                    if (!seen.Add(p.Name)) continue;
                    bool isReq = branches.All(br => br.TryGetProperty("required", out JsonElement rq)
                        && rq.EnumerateArray().Any(x => x.GetString() == p.Name));
                    string member = Pascal(p.Name);
                    if (member == name) member += "Value";
                    if (!emitted.Add(member))
                        throw new InvalidOperationException(
                            $"{name}: JSON property '{p.Name}' Pascal-folds to member '{member}', which already exists on this record.");
                    Doc(sb, "    ", p.Value);
                    sb.AppendLine($"    [JsonPropertyName(\"{p.Name}\")]");
                    sb.AppendLine($"    public {(isReq ? "required " : "")}{CsType(p.Value, isReq)} {member} {{ get; init; }}");
                }
            }
            sb.AppendLine("}");
        }
    }
    WriteGenerated(Path.Combine(outDir, "Types.g.cs"), sb.ToString());
}

// ---- Methods.g.cs (constants, protocol version, role interfaces) ---------------------------
{
    // method path -> (Request?, Response?, Notification?) from x-method carriers.
    Dictionary<string, (string? Req, string? Resp, string? Notif)> byMethod = new();
    foreach (string name in names)
    {
        JsonElement s = defs.GetProperty(name);
        if (!s.TryGetProperty("x-method", out JsonElement xm) || xm.ValueKind != JsonValueKind.String) continue;
        string m = xm.GetString()!;
        (string? req, string? resp, string? notif) = byMethod.TryGetValue(m, out (string? Req, string? Resp, string? Notif) cur) ? cur : (null, null, null);
        if (name.EndsWith("Request")) req = name;
        else if (name.EndsWith("Response")) resp = name;
        else if (name.EndsWith("Notification")) notif = name;
        byMethod[m] = (req, resp, notif);
    }

    string MethodName(string path) => Pascal(path) + "Async";

    void EmitInterface(StringBuilder sb, string iface, JsonElement methods)
    {
        sb.AppendLine();
        sb.AppendLine($"public interface {iface}");
        sb.AppendLine("{");
        foreach (JsonProperty m in methods.EnumerateObject())
        {
            string path = m.Value.GetString()!;
            if (!byMethod.TryGetValue(path, out (string? Req, string? Resp, string? Notif) t))
                throw new InvalidOperationException($"{iface}: method '{path}' has no x-method carrier in the schema.");
            string fn = MethodName(path);
            if (t.Notif is not null)
                sb.AppendLine($"    ValueTask {fn}({t.Notif} notification, CancellationToken cancellationToken = default);");
            else if (t.Req is not null && t.Resp is not null)
                sb.AppendLine($"    ValueTask<{t.Resp}> {fn}({t.Req} request, CancellationToken cancellationToken = default);");
            else
                throw new InvalidOperationException(
                    $"{iface}: method '{path}' carrier is neither a notification nor a request/response pair (req={t.Req}, resp={t.Resp}, notif={t.Notif}).");
        }
        sb.AppendLine("    ValueTask<JsonElement> ExtMethodAsync(string method, JsonElement @params, CancellationToken cancellationToken = default);");
        sb.AppendLine("    ValueTask ExtNotificationAsync(string method, JsonElement @params, CancellationToken cancellationToken = default);");
        sb.AppendLine("}");
    }

    void EmitConstants(StringBuilder sb, string cls, JsonElement methods)
    {
        sb.AppendLine();
        sb.AppendLine($"public static class {cls}");
        sb.AppendLine("{");
        foreach (JsonProperty m in methods.EnumerateObject())
            sb.AppendLine($"    public const string {Pascal(m.Name)} = \"{m.Value.GetString()}\";");
        sb.AppendLine("}");
    }

    JsonElement agentMethods = metaDoc.RootElement.GetProperty("agentMethods");
    JsonElement clientMethods = metaDoc.RootElement.GetProperty("clientMethods");
    int version = metaDoc.RootElement.GetProperty("version").GetInt32();

    StringBuilder sb = new(Header("using System.Threading;\nusing System.Threading.Tasks;\n"));
    sb.AppendLine();
    sb.AppendLine("public static class ProtocolConstants");
    sb.AppendLine("{");
    sb.AppendLine($"    public const int Version = {version};");
    sb.AppendLine("}");
    EmitConstants(sb, "AgentMethods", agentMethods);
    EmitConstants(sb, "ClientMethods", clientMethods);
    EmitInterface(sb, "IAcpAgent", agentMethods);
    EmitInterface(sb, "IAcpClient", clientMethods);
    WriteGenerated(Path.Combine(outDir, "Methods.g.cs"), sb.ToString());
}

Console.WriteLine($"Generated 4 files in {outDir}");
