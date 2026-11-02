using System.Diagnostics;
using System.Net;
using NUnit.Framework;

namespace Rhino.AI.Server.Tests;

[TestFixture]
public class AgentProxyEnvironmentTests
{
    private sealed class FakeSystemProxy(Uri? proxy, bool bypass = false) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }
        public Uri? Destination { get; private set; }
        public bool IsBypassed(Uri host) { Destination = host; return bypass; }
        public Uri GetProxy(Uri destination) { Destination = destination; return proxy ?? destination; }
    }

    [Test]
    public void InheritLeavesAllVariablesAloneAndDoesNotReadSystemProxy()
    {
        Dictionary<string, string?> environment = new() { ["HTTP_PROXY"] = "inherited", ["NO_PROXY"] = "*" };
        AgentProxyEnvironment.Apply(environment, AgentProxyEnvironment.Inherit, "", "codex",
            () => throw new AssertionException("System proxy must not be read"));
        Assert.That(environment, Is.EquivalentTo(new Dictionary<string, string?> { ["HTTP_PROXY"] = "inherited", ["NO_PROXY"] = "*" }));
    }

    [Test]
    public void CustomOverridesProxyAndKeepsExistingAndLoopbackBypasses()
    {
        Dictionary<string, string?> environment = new()
        {
            ["HTTP_PROXY"] = "old", ["ALL_PROXY"] = "old", ["NO_PROXY"] = "example.com,LOCALHOST,*",
            ["no_proxy"] = "example.com,internal.example",
        };
        AgentProxyEnvironment.Apply(environment, AgentProxyEnvironment.Custom, " http://127.0.0.1:7890/ ", "codex",
            () => throw new AssertionException("System proxy must not be read"));

        Assert.Multiple(() =>
        {
            Assert.That(environment["HTTPS_PROXY"], Is.EqualTo("http://127.0.0.1:7890"));
            Assert.That(environment["http_proxy"], Is.EqualTo("http://127.0.0.1:7890"));
            Assert.That(environment, Does.Not.ContainKey("ALL_PROXY"));
            Assert.That(environment["NO_PROXY"]!.Split(','), Is.EquivalentTo(new[] { "example.com", "LOCALHOST", "internal.example", "127.0.0.1", "::1", "[::1]" }));
            Assert.That(environment["no_proxy"], Is.EqualTo(environment["NO_PROXY"]));
            Assert.That(environment["NODE_USE_ENV_PROXY"], Is.EqualTo("1"));
        });
    }

    [Test]
    public void DirectClearsProxyVariablesAndRequestsBypassForEveryHost()
    {
        Dictionary<string, string?> environment = new() { ["HTTPS_PROXY"] = "old", ["all_proxy"] = "old", ["NO_PROXY"] = "old" };
        AgentProxyEnvironment.Apply(environment, AgentProxyEnvironment.Direct, "", "claude",
            () => throw new AssertionException("System proxy must not be read"));
        Assert.Multiple(() =>
        {
            Assert.That(environment, Does.Not.ContainKey("HTTPS_PROXY"));
            Assert.That(environment, Does.Not.ContainKey("all_proxy"));
            Assert.That(environment["NO_PROXY"], Is.EqualTo("*"));
        });
    }

    [TestCase("codex", "chatgpt.com")]
    [TestCase("claude", "api.anthropic.com")]
    [TestCase("gemini", "generativelanguage.googleapis.com")]
    public void SystemProxyResolvesForTheAgentsPrimaryService(string agent, string host)
    {
        FakeSystemProxy systemProxy = new(new Uri("http://proxy.example:3128"));
        Dictionary<string, string?> environment = new();
        AgentProxyEnvironment.Apply(environment, AgentProxyEnvironment.System, "", agent, () => systemProxy);
        Assert.Multiple(() =>
        {
            Assert.That(systemProxy.Destination!.Host, Is.EqualTo(host));
            Assert.That(environment["HTTPS_PROXY"], Is.EqualTo("http://proxy.example:3128"));
            Assert.That(environment["NO_PROXY"]!.Split(','), Does.Contain("localhost"));
        });
    }

    [Test]
    public void SystemBypassClearsInheritedProxyAndRequestsDirectAccess()
    {
        FakeSystemProxy systemProxy = new(new Uri("http://proxy.example:3128"), bypass: true);
        Dictionary<string, string?> environment = new() { ["HTTP_PROXY"] = "old" };
        AgentProxyEnvironment.Apply(environment, AgentProxyEnvironment.System, "", "codex", () => systemProxy);
        Assert.Multiple(() =>
        {
            Assert.That(environment, Does.Not.ContainKey("HTTP_PROXY"));
            Assert.That(environment["NO_PROXY"], Is.EqualTo("*"));
        });
    }

    [TestCase("")]
    [TestCase("localhost:7890")]
    [TestCase("socks5://localhost:1080")]
    [TestCase("http://user:secret@localhost:7890")]
    [TestCase("http://localhost:7890/path")]
    [TestCase("http://localhost:7890/?q=secret")]
    [TestCase("http://localhost:7890/#fragment")]
    public void InvalidOrCredentialedCustomAddressIsRejected(string address)
    {
        Assert.That(AgentProxyEnvironment.TryNormalizeAddress(address, out _), Is.False);
        Dictionary<string, string?> environment = new() { ["HTTP_PROXY"] = "unchanged" };
        Assert.Throws<AgentProxyException>(() => AgentProxyEnvironment.Apply(environment, AgentProxyEnvironment.Custom, address, "codex", () => throw new AssertionException("System proxy must not be read")));
        Assert.That(environment["HTTP_PROXY"], Is.EqualTo("unchanged"));
    }

    [Test]
    public void SystemResolutionFailureIsActionableAndDoesNotExposeItsDetails()
    {
        Dictionary<string, string?> environment = new() { ["HTTP_PROXY"] = "unchanged" };
        AgentProxyException? error = Assert.Throws<AgentProxyException>(() =>
            AgentProxyEnvironment.Apply(environment, AgentProxyEnvironment.System, "", "codex",
                () => throw new InvalidOperationException("sensitive system proxy details")));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("AI settings > Network"));
            Assert.That(error.Message, Does.Not.Contain("sensitive"));
            Assert.That(environment["HTTP_PROXY"], Is.EqualTo("unchanged"));
        });
    }

    [Test]
    public void CustomProxyIsVisibleToASpawnedChild()
    {
        bool windows = OperatingSystem.IsWindows();
        ProcessStartInfo start = new(windows ? "cmd.exe" : "/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        if (windows)
        {
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("echo %HTTPS_PROXY%");
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("printf '%s' \"$HTTPS_PROXY\"");
        }
        AgentProxyEnvironment.Apply(start.Environment, AgentProxyEnvironment.Custom, "http://127.0.0.1:7890", "codex", () => throw new AssertionException("System proxy must not be read"));
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Multiple(() =>
        {
            Assert.That(process.ExitCode, Is.Zero);
            Assert.That(output.Trim(), Is.EqualTo("http://127.0.0.1:7890"));
        });
    }
}
