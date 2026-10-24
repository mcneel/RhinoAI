using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using Rhino.AI.Router.Codegen;

namespace Rhino.AI.Router.Tests;

// Regression guard for the router source generator. Plugin tool sites write
// [McpServerTool(...)] positionally (name, title, readOnly, destructive). An
// earlier version only read named attribute args, so every tool's name came
// back null and zero proxies were emitted. These tests run the generator over
// synthetic tool files and assert a proxy IS produced.
[TestFixture]
public class CodegenTests
{
    [Test]
    public void Emits_proxy_for_positional_attribute()
    {
        string output = RunGenerator(
            """
            namespace Rhino.AI.Tools;

            [McpServerToolType]
            public class XTool
            {
                [McpServerTool("x", "X", true, false)]
                public static string Run(RhinoDoc doc) => "ok";
            }
            """);

        Assert.That(output, Does.Contain("public sealed class XToolProxy"));
        Assert.That(output, Does.Contain("Name = \"x\""));
        Assert.That(output, Does.Contain("Title = \"X\""));
        Assert.That(output, Does.Contain("ReadOnly = true"));
        Assert.That(output, Does.Contain("Destructive = false"));
        // Registrar must wire the proxy up so Program.cs picks it up.
        Assert.That(output, Does.Contain("WithTools<global::Rhino.AI.Router.Tools.Generated.XToolProxy>"));
    }

    [Test]
    public void Named_argument_overrides_positional_slot()
    {
        string output = RunGenerator(
            """
            namespace Rhino.AI.Tools;

            [McpServerToolType]
            public class YTool
            {
                [McpServerTool("ignored", Name = "real_name", Destructive = true)]
                public static string Run(RhinoDoc doc) => "ok";
            }
            """);

        Assert.That(output, Does.Contain("Name = \"real_name\""));
        Assert.That(output, Does.Not.Contain("Name = \"ignored\""));
        Assert.That(output, Does.Contain("Destructive = true"));
    }

    [Test]
    public void Folds_concatenated_description_literals()
    {
        // AskUserTool builds its [Description] with + concatenation across lines.
        // The generator must fold the constant string so the proxy carries the full
        // text, not an empty Description("").
        string output = RunGenerator(
            """
            namespace Rhino.AI.Tools;

            [McpServerToolType]
            public class ZTool
            {
                [McpServerTool("z", "Z", true, false)]
                [Description("first part " + "second part " + "third part")]
                public static string Run(
                    RhinoDoc doc,
                    [Description("flag on " + "two lines")] bool flag = false) => "ok";
            }
            """);

        Assert.That(output, Does.Contain("Description(\"first part second part third part\")"));
        Assert.That(output, Does.Contain("Description(\"flag on two lines\")"));
        Assert.That(output, Does.Not.Contain("Description(\"\")"));
    }

    [Test]
    public void Array_of_record_param_forwards_as_json_array()
    {
        // RH-96785: an array of a record type must map to an open array and forward
        // as a JsonArray. It used to map to an open object the plugin couldn't bind.
        string output = RunGenerator(
            """
            namespace Rhino.AI.Tools;

            [McpServerToolType]
            public class WTool
            {
                public record struct WireSpec(string SrcKey, string Src, string DstKey, string Dst);

                [McpServerTool("w", "W", false, false)]
                public static string Run(
                    RhinoDoc doc,
                    [Description("wires")] WireSpec[] wires) => "ok";
            }
            """);

        Assert.That(output, Does.Contain("List<global::System.Text.Json.JsonElement>? wires"));
        Assert.That(output, Does.Contain("var __arr_wires = new global::System.Text.Json.Nodes.JsonArray();"));
        Assert.That(output, Does.Contain("__arr_wires.Add(global::System.Text.Json.Nodes.JsonNode.Parse(__item.GetRawText()));"));
        Assert.That(output, Does.Not.Contain("__obj_wires"));
    }

    [Test]
    public void Decimal_param_passes_through_as_a_number()
    {
        // #172: g1_place_slider and g2_place_slider take decimal min/value/max. A
        // decimal used to map to an open object, so the proxy advertised "object"
        // and every call with a number failed to bind.
        string output = RunGenerator(
            """
            namespace Rhino.AI.Tools;

            [McpServerToolType]
            public class STool
            {
                [McpServerTool("s", "S", false, false)]
                public static string Run(
                    RhinoDoc doc,
                    [Description("min")] decimal min,
                    [Description("step")] decimal? step = null) => "ok";
            }
            """);

        Assert.That(output, Does.Contain("decimal min"));
        Assert.That(output, Does.Contain("decimal? step"));
        Assert.That(output, Does.Contain("args[\"min\"] = global::System.Text.Json.Nodes.JsonValue.Create(min);"));
        Assert.That(output, Does.Not.Contain("__obj_min"));

        // The router builds each proxy's schema from RouterJsonContext. Without
        // decimal declared there, it throws on startup instead of serving tools.
        Assert.That(RouterJsonContext.Default.GetTypeInfo(typeof(decimal)), Is.Not.Null);
        Assert.That(RouterJsonContext.Default.GetTypeInfo(typeof(decimal?)), Is.Not.Null);
    }

    private static string RunGenerator(string toolSource)
    {
        SyntaxTree compilationTree = CSharpSyntaxTree.ParseText("// placeholder compilation unit");
        CSharpCompilation compilation = CSharpCompilation.Create(
            "RouterCodegenTest",
            [compilationTree],
            references: [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The generator only scans AdditionalFiles whose path contains /plugin/Tools/.
        AdditionalText toolFile = new InMemoryAdditionalText(
            "/repo/rhino/plugin/Tools/SyntheticTool.cs",
            toolSource);

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new RouterToolGenerator().AsSourceGenerator()],
            additionalTexts: [toolFile]);

        GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();
        Assert.That(result.Diagnostics, Is.Empty);

        GeneratedSourceResult generated = result.Results
            .Single()
            .GeneratedSources
            .Single();

        return generated.SourceText.ToString();
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content);
    }
}
