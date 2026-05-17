using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RhMcp.Server;

namespace RhMcp.Server.Tests;

// Test-only resource types. Scan() discovers these via their attributes.
[McpServerResourceType]
internal sealed class FixtureResources
{
    [McpServerResource(UriTemplate = "rhino://config/version", Name = "version")]
    public static string GetVersion() => "1.0";

    [McpServerResource(UriTemplate = "rhino://config/{key}", Name = "byKey")]
    public static string GetByKey(string key) => key;

    [McpServerResource(UriTemplate = "rhino://docs/{section}/{page}", Name = "doc")]
    public static string GetDoc(string section, string page) => $"{section}/{page}";

    [McpServerResource(UriTemplate = "rhino://about", Name = "about", MimeType = "text/plain")]
    public static string GetAbout() => "about";
}

internal sealed class NotADecoratedType
{
    [McpServerResource(UriTemplate = "rhino://ignored")]
    public static string Ignored() => "ignored";
}

[TestFixture]
internal class ResourceRegistryTests
{
    private static IServiceProvider EmptyServices()
        => new ServiceCollection().BuildServiceProvider();

    private static ResourceRegistry BuildRegistry()
        => ResourceRegistry.Scan(typeof(FixtureResources).Assembly, EmptyServices());

    [Test]
    public void Scan_discovers_resources_on_decorated_types_only()
    {
        ResourceRegistry registry = BuildRegistry();
        string[] templates = registry.All.Select(h => h.UriTemplate).ToArray();
        Assert.That(templates, Does.Contain("rhino://config/version"));
        Assert.That(templates, Does.Contain("rhino://config/{key}"));
        Assert.That(templates, Does.Contain("rhino://docs/{section}/{page}"));
        Assert.That(templates, Does.Contain("rhino://about"));
        Assert.That(templates, Does.Not.Contain("rhino://ignored"),
            "methods on undecorated types must not be registered");
    }

    [Test]
    public void StaticResources_excludes_templated_handlers()
    {
        ResourceRegistry registry = BuildRegistry();
        string[] templates = registry.StaticResources.Select(h => h.UriTemplate).ToArray();
        Assert.That(templates, Is.EquivalentTo(new[]
        {
            "rhino://config/version",
            "rhino://about",
        }));
    }

    [Test]
    public void Templated_excludes_static_handlers()
    {
        ResourceRegistry registry = BuildRegistry();
        string[] templates = registry.Templated.Select(h => h.UriTemplate).ToArray();
        Assert.That(templates, Is.EquivalentTo(new[]
        {
            "rhino://config/{key}",
            "rhino://docs/{section}/{page}",
        }));
    }

    [Test]
    public void TryMatch_static_exact_match_returns_empty_variables()
    {
        ResourceHandler h = new(
            method: typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetAbout))!,
            uriTemplate: "rhino://about",
            name: "about",
            description: null,
            mimeType: null,
            services: EmptyServices());

        bool matched = h.TryMatch("rhino://about", out IReadOnlyDictionary<string, string> vars);
        Assert.That(matched, Is.True);
        Assert.That(vars, Is.Empty);
    }

    [Test]
    public void TryMatch_static_rejects_other_uris()
    {
        ResourceHandler h = new(
            method: typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetAbout))!,
            uriTemplate: "rhino://about",
            name: "about",
            description: null,
            mimeType: null,
            services: EmptyServices());

        Assert.That(h.TryMatch("rhino://about/extra", out _), Is.False);
        Assert.That(h.TryMatch("rhino://other", out _), Is.False);
    }

    [Test]
    public void TryMatch_template_captures_named_variable()
    {
        ResourceHandler h = new(
            method: typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetByKey))!,
            uriTemplate: "rhino://config/{key}",
            name: "byKey",
            description: null,
            mimeType: null,
            services: EmptyServices());

        bool matched = h.TryMatch("rhino://config/theme", out IReadOnlyDictionary<string, string> vars);
        Assert.That(matched, Is.True);
        Assert.That(vars["key"], Is.EqualTo("theme"));
    }

    [Test]
    public void TryMatch_template_captures_multiple_variables()
    {
        ResourceHandler h = new(
            method: typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetDoc))!,
            uriTemplate: "rhino://docs/{section}/{page}",
            name: "doc",
            description: null,
            mimeType: null,
            services: EmptyServices());

        bool matched = h.TryMatch("rhino://docs/intro/install", out IReadOnlyDictionary<string, string> vars);
        Assert.That(matched, Is.True);
        Assert.That(vars["section"], Is.EqualTo("intro"));
        Assert.That(vars["page"], Is.EqualTo("install"));
    }

    [Test]
    public void TryMatch_template_does_not_cross_slashes()
    {
        ResourceHandler h = new(
            method: typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetByKey))!,
            uriTemplate: "rhino://config/{key}",
            name: "byKey",
            description: null,
            mimeType: null,
            services: EmptyServices());

        Assert.That(h.TryMatch("rhino://config/a/b", out _), Is.False,
            "template variables match anything except '/'");
    }

    [Test]
    public void TryMatch_template_rejects_partial_uri()
    {
        ResourceHandler h = new(
            method: typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetByKey))!,
            uriTemplate: "rhino://config/{key}",
            name: "byKey",
            description: null,
            mimeType: null,
            services: EmptyServices());

        Assert.That(h.TryMatch("rhino://config/", out _), Is.False,
            "the variable segment is at least one char");
        Assert.That(h.TryMatch("rhino://other/value", out _), Is.False);
    }

    [Test]
    public void Constructor_rejects_empty_variable_name()
    {
        MethodInfo method = typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetByKey))!;
        Assert.Throws<InvalidOperationException>(() => new ResourceHandler(
            method,
            uriTemplate: "rhino://config/{}",
            name: "bad",
            description: null,
            mimeType: null,
            services: EmptyServices()));
    }

    [Test]
    public void Constructor_rejects_unterminated_brace()
    {
        MethodInfo method = typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetByKey))!;
        Assert.Throws<InvalidOperationException>(() => new ResourceHandler(
            method,
            uriTemplate: "rhino://config/{key",
            name: "bad",
            description: null,
            mimeType: null,
            services: EmptyServices()));
    }

    // Register the templated handler first to defeat any incidental ordering
    // from reflection — the static handler must still win.
    [Test]
    public void Match_prefers_static_over_templated_when_both_apply()
    {
        ResourceRegistry registry = new();
        IServiceProvider sp = EmptyServices();
        registry.Add(new ResourceHandler(
            typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetByKey))!,
            "rhino://config/{key}", "byKey", null, null, sp));
        registry.Add(new ResourceHandler(
            typeof(FixtureResources).GetMethod(nameof(FixtureResources.GetVersion))!,
            "rhino://config/version", "version", null, null, sp));

        ResourceHandler? matched = registry.Match("rhino://config/version", out IReadOnlyDictionary<string, string> vars);
        Assert.That(matched, Is.Not.Null);
        Assert.That(matched!.UriTemplate, Is.EqualTo("rhino://config/version"),
            "exact-URI handler should win over a templated handler that also matches");
        Assert.That(vars, Is.Empty);
    }
}
