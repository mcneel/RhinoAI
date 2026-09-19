using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using Rhino.AI.Server;
using Rhino.AI.Tools;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class ParameterBinderTests
{
    private enum Mode { On, Off }

    private interface IGreeter { string Hello(); }
    private sealed class Greeter : IGreeter { public string Hello() => "hi"; }

    private class SampleMethods
    {
        public void M(
            string name,
            int count,
            int? maybeCount,
            string? nullableName,
            Mode mode,
            System.Guid id,
            string label = "fallback",
            int retries = 3,
            bool flag = false) { }

        public void Service(IGreeter greeter) { }
        public void Cancel(System.Threading.CancellationToken ct) { }
        public void UriT(string slug) { }
        public void NoArgs() { }
    }

    private record struct Wire(string SrcKey, string Src, string Dst);

    private static ParameterDescriptor Desc(string param, ParameterBindingKind kind)
    {
        ParameterInfo pi = typeof(SampleMethods).GetMethod(nameof(SampleMethods.M))!
            .GetParameters().Single(p => p.Name == param);
        return new ParameterDescriptor(pi, kind);
    }

    private static ParameterDescriptor DescFrom(string method, string param, ParameterBindingKind kind)
    {
        ParameterInfo pi = typeof(SampleMethods).GetMethod(method)!
            .GetParameters().Single(p => p.Name == param);
        return new ParameterDescriptor(pi, kind);
    }

    private static ParameterDescriptor[] Descriptors(string method)
        => typeof(SampleMethods).GetMethod(method)!
            .GetParameters()
            .Select(pi => new ParameterDescriptor(pi, ParameterBindingKind.Argument))
            .ToArray();

    private static Dictionary<string, JsonElement> Args(string json)
    {
        JsonDocument doc = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> dict = new();
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    private static IServiceProvider EmptyServices()
        => new StubServices();

    // ----- Argument binding ------------------------------------------------

    [Test]
    public void Argument_present_deserialises_value()
    {
        object? value = ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": 42 }"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void Argument_missing_uses_default_value()
    {
        object? value = ParameterBinder.Resolve(
            Desc("retries", ParameterBindingKind.Argument),
            Args("""{}"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.EqualTo(3));
    }

    [Test]
    public void Argument_missing_with_no_default_and_non_nullable_value_type_throws()
    {
        ArgumentBindingException ex = Assert.Throws<ArgumentBindingException>(() => ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{}"""),
            EmptyServices(),
            default))!;
        Assert.That(ex.WireName, Is.EqualTo("count"));
        Assert.That(ex.Problem, Does.Contain("was not supplied"));
    }

    [Test]
    public void Argument_null_for_nullable_value_type_yields_null()
    {
        object? value = ParameterBinder.Resolve(
            Desc("maybeCount", ParameterBindingKind.Argument),
            Args("""{ "maybeCount": null }"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.Null);
    }

    [Test]
    public void Argument_null_for_non_nullable_value_type_throws()
    {
        ArgumentBindingException ex = Assert.Throws<ArgumentBindingException>(() => ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": null }"""),
            EmptyServices(),
            default))!;
        Assert.That(ex.WireName, Is.EqualTo("count"));
    }

    [Test]
    public void Argument_null_for_non_nullable_reference_type_throws()
    {
        ArgumentBindingException ex = Assert.Throws<ArgumentBindingException>(() => ParameterBinder.Resolve(
            Desc("name", ParameterBindingKind.Argument),
            Args("""{ "name": null }"""),
            EmptyServices(),
            default))!;
        Assert.That(ex.WireName, Is.EqualTo("name"));
    }

    [Test]
    public void Argument_null_for_nullable_reference_type_yields_null()
    {
        object? value = ParameterBinder.Resolve(
            Desc("nullableName", ParameterBindingKind.Argument),
            Args("""{ "nullableName": null }"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.Null);
    }

    [Test]
    public void Argument_number_binds_to_string()
    {
        object? value = ParameterBinder.Resolve(
            Desc("name", ParameterBindingKind.Argument),
            Args("""{ "name": 42 }"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.EqualTo("42"));
    }

    [Test]
    public void Argument_bool_binds_to_string()
    {
        object? value = ParameterBinder.Resolve(
            Desc("name", ParameterBindingKind.Argument),
            Args("""{ "name": true }"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.EqualTo("true"));
    }

    [Test]
    public void Nested_string_field_coerces_number_inside_array()
    {
        Wire[]? wires = JsonSerializer.Deserialize<Wire[]>(
            """[ { "srcKey": "a", "src": 0, "dst": "R" } ]""",
            McpSerializer.Options);
        Assert.That(wires, Is.Not.Null);
        Assert.That(wires![0].Src, Is.EqualTo("0"));
        Assert.That(wires[0].Dst, Is.EqualTo("R"));
    }

    [Test]
    public void Options_still_deserialises_string_keyed_dictionary()
    {
        CallToolRequestParams? p = JsonSerializer.Deserialize<CallToolRequestParams>(
            """{ "name": "g1_apply_graph", "arguments": { "sliders": [], "components": [] } }""",
            McpSerializer.Options);
        Assert.That(p, Is.Not.Null);
        Assert.That(p!.Name, Is.EqualTo("g1_apply_graph"));
        Assert.That(p.Arguments!.ContainsKey("sliders"), Is.True);
        Assert.That(p.Arguments.ContainsKey("components"), Is.True);
    }

    [Test]
    public void Argument_string_binds_to_bool()
    {
        object? value = ParameterBinder.Resolve(
            Desc("flag", ParameterBindingKind.Argument),
            Args("""{ "flag": "true" }"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.EqualTo(true));
    }

    [Test]
    public void Number_for_bool_reports_the_expected_type_and_the_value_sent()
    {
        ArgumentBindingException ex = Assert.Throws<ArgumentBindingException>(() => ParameterBinder.Resolve(
            Desc("flag", ParameterBindingKind.Argument),
            Args("""{ "flag": 1 }"""),
            EmptyServices(),
            default))!;
        Assert.That(ex.WireName, Is.EqualTo("flag"));
        Assert.That(ex.Problem, Is.EqualTo("argument 'flag' expects boolean but got 1"));
        Assert.That(ex.InnerException, Is.TypeOf<JsonException>());
    }

    [Test]
    public void Argument_integral_decimal_binds_to_int()
    {
        object? value = ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": 3.0 }"""),
            EmptyServices(),
            default);
        Assert.That(value, Is.EqualTo(3));
    }

    [Test]
    public void Fractional_number_for_int_rounds_and_notes_the_change()
    {
        using BindNotes.Call call = BindNotes.Begin();

        object? value = ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": 3.7 }"""),
            EmptyServices(),
            default);

        Assert.That(value, Is.EqualTo(4));
        Assert.That(call.Notes, Is.EqualTo(new[] { "count 3.7 was rounded to 4" }));
    }

    [Test]
    public void Fractional_midpoint_rounds_away_from_zero()
    {
        using BindNotes.Call call = BindNotes.Begin();

        object? positive = ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument), Args("""{ "count": 2.5 }"""), EmptyServices(), default);
        object? negative = ParameterBinder.Resolve(
            Desc("retries", ParameterBindingKind.Argument), Args("""{ "retries": -2.5 }"""), EmptyServices(), default);

        Assert.That(positive, Is.EqualTo(3));
        Assert.That(negative, Is.EqualTo(-3));
        Assert.That(call.Notes, Is.EqualTo(new[] { "count 2.5 was rounded to 3", "retries -2.5 was rounded to -3" }));
    }

    [Test]
    public void Integral_decimal_binds_without_a_note()
    {
        using BindNotes.Call call = BindNotes.Begin();

        object? value = ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": 3.0 }"""),
            EmptyServices(),
            default);

        Assert.That(value, Is.EqualTo(3));
        Assert.That(call.Notes, Is.Empty);
    }

    [Test]
    public void Rounding_outside_a_note_scope_still_binds()
    {
        object? value = ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": 3.7 }"""),
            EmptyServices(),
            default);

        Assert.That(value, Is.EqualTo(4));
    }

    [Test]
    public void Unparseable_int_reports_the_expected_type_and_the_value_sent()
    {
        ArgumentBindingException ex = Assert.Throws<ArgumentBindingException>(() => ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": "abc" }"""),
            EmptyServices(),
            default))!;
        Assert.That(ex.Problem, Is.EqualTo("argument 'count' expects integer but got \"abc\""));
    }

    // One bind error per round trip was the same tax the tools used to charge: a model that gets the
    // vector shape wrong gets it wrong for every vector argument at once.
    [Test]
    public void Every_bad_argument_is_reported_together()
    {
        ArgumentBindingException[] failures =
        [
            new("count", "argument 'count' expects integer but got \"abc\""),
            new("flag", "argument 'flag' expects boolean but got 1"),
        ];

        IToolResult result = BindingFailure.Describe("demo_tool", Descriptors(nameof(SampleMethods.M)), failures);

        Assert.That(result.Message, Does.Contain("argument 'count' expects integer"));
        Assert.That(result.Message, Does.Contain("argument 'flag' expects boolean"));
        Assert.That(result.Message, Does.StartWith("demo_tool: "));
    }

    [Test]
    public void Binding_failure_names_the_tool_and_its_arguments()
    {
        ArgumentBindingException failure = new("count", "required argument 'count' was not supplied");

        IToolResult result = BindingFailure.Describe("demo_tool", Descriptors(nameof(SampleMethods.M)), failure);

        Assert.That(result.Code, Is.EqualTo(ToolError.BadArgument));
        Assert.That(result.Message, Is.EqualTo("demo_tool: required argument 'count' was not supplied"));
        Assert.That(result.Guidance, Is.EqualTo(
            "'demo_tool' requires name (string), count (integer), mode (integer), id (string). "
            + "Optional: maybeCount (integer), nullableName (string), label (string), retries (integer), flag (boolean)."));
    }

    [Test]
    public void Binding_failure_on_a_tool_without_arguments_says_so()
    {
        ArgumentBindingException failure = new("count", "required argument 'count' was not supplied");

        IToolResult result = BindingFailure.Describe("demo_tool", Descriptors(nameof(SampleMethods.NoArgs)), failure);

        Assert.That(result.Guidance, Is.EqualTo("'demo_tool' takes no arguments."));
    }

    [Test]
    public void Integer_json_still_binds_to_double()
    {
        double value = JsonSerializer.Deserialize<double>("5", McpSerializer.Options);
        Assert.That(value, Is.EqualTo(5.0));
    }

    [Test]
    public void Enum_still_reads_from_number()
    {
        Mode value = JsonSerializer.Deserialize<Mode>("1", McpSerializer.Options);
        Assert.That(value, Is.EqualTo(Mode.Off));
    }

    [Test]
    public void Nullable_value_types_use_the_lenient_converters()
    {
        bool? flag = JsonSerializer.Deserialize<bool?>("\"false\"", McpSerializer.Options);
        int? count = JsonSerializer.Deserialize<int?>("2.0", McpSerializer.Options);
        Assert.That(flag, Is.EqualTo(false));
        Assert.That(count, Is.EqualTo(2));
    }

    // ----- Service binding -------------------------------------------------

    [Test]
    public void Service_resolves_from_provider()
    {
        Greeter greeter = new();
        IServiceProvider sp = new StubServices(greeter);

        object? value = ParameterBinder.Resolve(
            DescFrom(nameof(SampleMethods.Service), "greeter", ParameterBindingKind.Service),
            arguments: null,
            sp,
            default);
        Assert.That(value, Is.SameAs(greeter));
    }

    [Test]
    public void Service_missing_with_no_default_throws()
    {
        // Tighten the assertion to the binder's own error path — if a future
        // change routes through GetRequiredService instead, that would throw
        // InvalidOperationException and slip past a base-Exception check.
        ArgumentException ex = Assert.Throws<ArgumentException>(() => ParameterBinder.Resolve(
            DescFrom(nameof(SampleMethods.Service), "greeter", ParameterBindingKind.Service),
            arguments: null,
            EmptyServices(),
            default))!;
        Assert.That(ex.Message, Does.Contain("No service of type"));
    }

    // ----- CancellationToken binding --------------------------------------

    [Test]
    public void CancellationToken_is_passed_through()
    {
        using System.Threading.CancellationTokenSource cts = new();
        object? value = ParameterBinder.Resolve(
            DescFrom(nameof(SampleMethods.Cancel), "ct", ParameterBindingKind.CancellationToken),
            arguments: null,
            EmptyServices(),
            cts.Token);
        Assert.That(value, Is.EqualTo(cts.Token));
    }

    // ----- URI template binding -------------------------------------------

    [Test]
    public void UriTemplate_value_resolves_from_dictionary()
    {
        Dictionary<string, string> vars = new() { ["slug"] = "alpha" };
        object? value = ParameterBinder.Resolve(
            DescFrom(nameof(SampleMethods.UriT), "slug", ParameterBindingKind.UriTemplate),
            arguments: null,
            EmptyServices(),
            default,
            vars);
        Assert.That(value, Is.EqualTo("alpha"));
    }

    [Test]
    public void UriTemplate_missing_with_no_default_throws()
    {
        Assert.Throws<ArgumentException>(() => ParameterBinder.Resolve(
            DescFrom(nameof(SampleMethods.UriT), "slug", ParameterBindingKind.UriTemplate),
            arguments: null,
            EmptyServices(),
            default,
            uriTemplateValues: new Dictionary<string, string>()));
    }
}

