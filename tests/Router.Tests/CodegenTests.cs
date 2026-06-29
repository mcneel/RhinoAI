using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using RhMcp.Router.Codegen;

namespace RhMcp.Router.Tests;

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
            namespace RhMcp.Tools;

            [McpServerToolType]
            public class XTool
            {
                [McpServerTool("x", "X", true, false)]
                public static string Run(RhinoDoc doc) => "ok";
            }
            """);

        Assert.That(output, Does.Contain("public class XToolProxy"));
        Assert.That(output, Does.Contain("Name = \"x\""));
        Assert.That(output, Does.Contain("Title = \"X\""));
        Assert.That(output, Does.Contain("ReadOnly = true"));
        Assert.That(output, Does.Contain("Destructive = false"));
        // Registrar must wire the proxy up so Program.cs picks it up.
        Assert.That(output, Does.Contain("WithTools<global::RhMcp.Router.Tools.Generated.XToolProxy>"));
    }

    [Test]
    public void Named_argument_overrides_positional_slot()
    {
        string output = RunGenerator(
            """
            namespace RhMcp.Tools;

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
            namespace RhMcp.Tools;

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
