using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RhinoAI.Server;
using RhinoAI.Tools;

namespace RhinoAI.Server.Tests;

[TestFixture]
public class SchemaBuilderTests
{
    private enum Color { Red, Green, Blue }

    private sealed record Node(string Name, Node? Child);

    private class SampleMethods
    {
        // The real ask_user signature: a batch of questions, so the schema below is the one the
        // agent actually reads.
        public void Batch(QuestionSpec[] questions) { }
        public void Nested(Node root) { }

        public void Required(
            string name,
            int count,
            int? maybeCount,
            string label = "x",
            bool flag = false) { }

        public void Types(
            string s, bool b, int i, long l, double d, decimal m,
            System.Guid g, System.DateTime dt, System.TimeSpan ts,
            Color color,
            int[] ints,
            List<string> strs,
            IEnumerable<int> nums,
            Dictionary<string, int> map,
            SampleMethods complex) { }
    }

    private static ParameterDescriptor Arg(string method, string param)
    {
        ParameterInfo pi = typeof(SampleMethods).GetMethod(method)!
            .GetParameters().Single(p => p.Name == param);
        return new ParameterDescriptor(pi, ParameterBindingKind.Argument);
    }

    private static JsonElement Build(params ParameterDescriptor[] descriptors)
        => SchemaBuilder.BuildInputSchema(descriptors);

    [Test]
    public void Schema_root_is_object_with_properties()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Required), "name"));
        Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));
        Assert.That(schema.TryGetProperty("properties", out _), Is.True);
    }

    [Test]
    public void Required_array_includes_value_type_with_no_default()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Required), "count"));
        Assert.That(schema.TryGetProperty("required", out JsonElement required), Is.True);
        string[] names = required.EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.That(names, Does.Contain("count"));
    }

    [Test]
    public void Required_array_omits_value_type_with_default()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Required), "flag"));
        Assert.That(schema.TryGetProperty("required", out _), Is.False,
            "params with default values must not appear in `required`");
    }

    [Test]
    public void Required_array_omits_nullable_value_type()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Required), "maybeCount"));
        Assert.That(schema.TryGetProperty("required", out _), Is.False,
            "Nullable<T> params are implicitly optional");
    }

    [Test]
    public void Required_array_includes_non_nullable_reference_type_with_no_default()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Required), "name"));
        Assert.That(schema.TryGetProperty("required", out JsonElement required), Is.True,
            "string parameter without a default should be required");
        string[] names = required.EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.That(names, Does.Contain("name"));
    }

    [Test]
    public void Required_array_omitted_when_no_params_are_required()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Required), "label"));
        Assert.That(schema.TryGetProperty("required", out _), Is.False);
    }

    [TestCase("s", "string")]
    [TestCase("b", "boolean")]
    [TestCase("i", "integer")]
    [TestCase("l", "integer")]
    [TestCase("d", "number")]
    [TestCase("m", "number")]
    [TestCase("g", "string")]
    [TestCase("dt", "string")]
    [TestCase("ts", "string")]
    [TestCase("ints", "array")]
    [TestCase("strs", "array")]
    [TestCase("nums", "array")]
    [TestCase("complex", "object")]
    public void MapType_emits_expected_json_type(string paramName, string expected)
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Types), paramName));
        string actual = schema.GetProperty("properties").GetProperty(paramName)
            .GetProperty("type").GetString()!;
        Assert.That(actual, Is.EqualTo(expected));
    }

    // Round-trip: whatever representation the schema advertises for an enum,
    // the binder must accept that same representation. Catches drift between
    // SchemaBuilder.MapType and McpSerializer.Options enum-converter setup.
    [Test]
    public void Enum_schema_and_binder_agree_on_representation()
    {
        ParameterDescriptor desc = Arg(nameof(SampleMethods.Types), "color");
        JsonElement schema = Build(desc);
        string schemaType = schema.GetProperty("properties").GetProperty("color")
            .GetProperty("type").GetString()!;

        string argJson = schemaType switch
        {
            "integer" => """{ "color": 1 }""",
            "string" => """{ "color": "Green" }""",
            _ => throw new AssertionException($"Unsupported enum schema type '{schemaType}'"),
        };
        JsonDocument doc = JsonDocument.Parse(argJson);
        Dictionary<string, JsonElement> args = new();
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            args[prop.Name] = prop.Value.Clone();

        object? value = ParameterBinder.Resolve(
            desc, args, new ServiceCollection().BuildServiceProvider(), default);
        Assert.That(value, Is.EqualTo(Color.Green));
    }

    // Dictionary<,> falls through to "object" with no inner shape. It must
    // not be advertised as "array" — that would mislead clients.
    [Test]
    public void Dictionary_param_is_object_not_array()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Types), "map"));
        string actual = schema.GetProperty("properties").GetProperty("map")
            .GetProperty("type").GetString()!;
        Assert.That(actual, Is.EqualTo("object"));
    }

    // A tool taking a list of records is unusable without `items`: the agent has to guess the
    // element shape, and ask_user is exactly that tool.
    [Test]
    public void An_array_of_records_advertises_its_element_shape()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Batch), "questions"));
        JsonElement questions = schema.GetProperty("properties").GetProperty("questions");
        Assert.That(questions.GetProperty("type").GetString(), Is.EqualTo("array"));

        JsonElement items = questions.GetProperty("items");
        Assert.That(items.GetProperty("type").GetString(), Is.EqualTo("object"));

        JsonElement props = items.GetProperty("properties");
        Assert.That(props.GetProperty("question").GetProperty("type").GetString(), Is.EqualTo("string"));
        Assert.That(props.GetProperty("options").GetProperty("type").GetString(), Is.EqualTo("array"));
        Assert.That(props.GetProperty("options").GetProperty("items").GetProperty("type").GetString(), Is.EqualTo("string"));
        Assert.That(props.GetProperty("multiSelect").GetProperty("type").GetString(), Is.EqualTo("boolean"));
    }

    // The names in the schema have to be the names the binder reads, or a well-formed call silently
    // binds nothing.
    [Test]
    public void Nested_property_names_use_the_serializers_naming_policy()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Batch), "questions"));
        JsonElement props = schema.GetProperty("properties").GetProperty("questions")
            .GetProperty("items").GetProperty("properties");
        string[] names = props.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "question", "options", "multiSelect" }));
    }

    [Test]
    public void Nested_required_follows_the_same_rules_as_a_tool_parameter()
    {
        JsonElement items = Build(Arg(nameof(SampleMethods.Batch), "questions"))
            .GetProperty("properties").GetProperty("questions").GetProperty("items");
        string[] required = items.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.That(required, Is.EqualTo(new[] { "question", "options" }),
            "multiSelect has a default, so it is optional");
    }

    [Test]
    public void Nested_descriptions_reach_the_agent()
    {
        JsonElement props = Build(Arg(nameof(SampleMethods.Batch), "questions"))
            .GetProperty("properties").GetProperty("questions")
            .GetProperty("items").GetProperty("properties");
        Assert.That(props.GetProperty("question").GetProperty("description").GetString(),
            Does.Contain("question to show"));
        Assert.That(props.GetProperty("multiSelect").GetProperty("description").GetString(),
            Does.Contain("checkboxes"));
    }

    // A self-referencing type must terminate rather than blow the stack, and it stops at a bare
    // object rather than unrolling forever.
    [Test]
    public void A_recursive_type_terminates()
    {
        JsonElement root = Build(Arg(nameof(SampleMethods.Nested), "root")).GetProperty("properties").GetProperty("root");
        JsonElement child = root.GetProperty("properties").GetProperty("child");
        Assert.That(child.GetProperty("type").GetString(), Is.EqualTo("object"));
        Assert.That(child.TryGetProperty("properties", out _), Is.False);
    }

    // Dictionary<,>'s constructors take capacities and comparers; advertising those as fields would
    // be a lie, so framework types stay shapeless.
    [Test]
    public void A_framework_type_is_not_given_an_invented_shape()
    {
        JsonElement map = Build(Arg(nameof(SampleMethods.Types), "map")).GetProperty("properties").GetProperty("map");
        Assert.That(map.TryGetProperty("properties", out _), Is.False);
    }

    [Test]
    public void Service_and_cancellation_params_are_excluded_from_schema()
    {
        ParameterInfo nameParam = typeof(SampleMethods).GetMethod(nameof(SampleMethods.Required))!
            .GetParameters().Single(p => p.Name == "name");
        ParameterDescriptor service = new(nameParam, ParameterBindingKind.Service);
        ParameterDescriptor ct = new(nameParam, ParameterBindingKind.CancellationToken);
        ParameterDescriptor templ = new(nameParam, ParameterBindingKind.UriTemplate);

        JsonElement schema = Build(service, ct, templ);
        JsonElement props = schema.GetProperty("properties");
        Assert.That(props.EnumerateObject().Count(), Is.EqualTo(0));
    }
}
