using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using RhMcp.Server;

namespace RhMcp.Server.Tests;

[TestFixture]
public class SchemaBuilderTests
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

    // Documents bug F-NEW SchemaBuilder.cs:104. A non-nullable reference-type
    // parameter with no default *should* appear in `required`. Today's
    // implementation short-circuits on !IsValueType and returns false, so this
    // test will fail until IsRequired honours NRT or treats reference types as
    // required-by-default.
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

    // Documents bug F-NEW SchemaBuilder.cs:67. Enum types are advertised as
    // "string" but McpSerializer.Options does not register a string enum
    // converter, so the binder only accepts integers. Pick one canonical
    // representation; this test pins the schema to whatever the binder accepts.
    [Test]
    public void Enum_schema_type_matches_what_binder_accepts()
    {
        JsonElement schema = Build(Arg(nameof(SampleMethods.Types), "color"));
        string actual = schema.GetProperty("properties").GetProperty("color")
            .GetProperty("type").GetString()!;
        Assert.That(actual, Is.EqualTo("string").Or.EqualTo("integer"),
            "schema must not advertise a representation the binder cannot read");
    }

    // Documents bug F-NEW SchemaBuilder.cs:69. Dictionary<,> falls through to
    // "object" with no inner shape — arguably fine, but it shouldn't claim to
    // be an "array" either. Pin the current shallow-object behaviour.
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
