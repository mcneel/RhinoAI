using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RhMcp.Server;

namespace RhMcp.Server.Tests;

[TestFixture]
internal class ParameterBinderTests
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
            int retries = 3) { }

        public void Service(IGreeter greeter) { }
        public void Cancel(System.Threading.CancellationToken ct) { }
        public void UriT(string slug) { }
    }

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

    private static Dictionary<string, JsonElement> Args(string json)
    {
        JsonDocument doc = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> dict = new();
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    private static IServiceProvider EmptyServices()
        => new ServiceCollection().BuildServiceProvider();

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
        Assert.Throws<ArgumentException>(() => ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{}"""),
            EmptyServices(),
            default));
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
        Assert.Throws<ArgumentException>(() => ParameterBinder.Resolve(
            Desc("count", ParameterBindingKind.Argument),
            Args("""{ "count": null }"""),
            EmptyServices(),
            default));
    }

    [Test]
    public void Argument_null_for_non_nullable_reference_type_throws()
    {
        Assert.Throws<ArgumentException>(() => ParameterBinder.Resolve(
            Desc("name", ParameterBindingKind.Argument),
            Args("""{ "name": null }"""),
            EmptyServices(),
            default));
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

    // ----- Service binding -------------------------------------------------

    [Test]
    public void Service_resolves_from_provider()
    {
        ServiceCollection sc = new();
        Greeter greeter = new();
        sc.AddSingleton<IGreeter>(greeter);
        IServiceProvider sp = sc.BuildServiceProvider();

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

