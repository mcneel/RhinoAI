using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RhMcp.Server;

namespace RhMcp.Server.Tests;

[TestFixture]
internal class SchemaBuilderTests
{
    private enum Color { Red, Green, Blue }

    private class SampleMethods
    {
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
