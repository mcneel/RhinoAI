using System.Diagnostics;
using System.Net;

namespace Rhino.AI;

// Configures only the spawned agent, never Rhino's own process or the machine environment.
// The system proxy is resolved for the agent's service at launch; PAC files may return
// different proxies for other destinations, which a process-wide environment cannot represent.
internal static class AgentProxyEnvironment
{
    public const string Inherit = "inherit";
    public const string System = "system";
    public const string Custom = "custom";
    public const string Direct = "direct";

    private static readonly string[] ProxyKeys =
        ["HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy"];
    private static readonly string[] Loopback = ["localhost", "127.0.0.1", "::1", "[::1]"];

    public static bool IsMode(string mode) => mode is Inherit or System or Custom or Direct;

    // Credentials are deliberately not stored in Rhino settings. An authenticated proxy can
    // instead be supplied through the inherited environment.
    public static bool TryNormalizeAddress(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
            return false;

        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    public static void Configure(ProcessStartInfo start, string mode, string address, string agent)
    {
#pragma warning disable SYSLIB0014 // This API reads the OS proxy, rather than inherited proxy environment variables.
        Apply(start.Environment, mode, address, agent, () => WebRequest.GetSystemWebProxy());
#pragma warning restore SYSLIB0014
    }

    // A factory keeps system/PAC resolution out of the default path and makes it testable.
    internal static void Apply(IDictionary<string, string?> environment, string mode, string address,
        string agent, Func<IWebProxy> systemProxy)
    {
        if (mode == Inherit || !IsMode(mode))
            return;

        string? proxy = null;
        if (mode == Custom)
        {
            if (!TryNormalizeAddress(address, out string normalized))
                throw new AgentProxyException("The custom proxy address is invalid. Check AI settings > Network.");
            proxy = normalized;
        }
        else if (mode == System)
        {
            try
            {
                Uri destination = AgentDestination(agent);
                IWebProxy settings = systemProxy();
                if (!settings.IsBypassed(destination))
                {
                    Uri? resolved = settings.GetProxy(destination);
                    if (resolved is not null && resolved != destination)
                    {
                        if (!TryNormalizeAddress(resolved.ToString(), out string normalized))
                            throw new AgentProxyException("The system proxy is not a supported HTTP or HTTPS proxy. Check AI settings > Network.");
                        proxy = normalized;
                    }
                }
            }
            catch (AgentProxyException) { throw; }
            catch (Exception ex)
            {
                throw new AgentProxyException("Could not resolve the system proxy. Check AI settings > Network.", ex);
            }
        }

        string bypass = mode == Direct || (mode == System && proxy is null)
            ? "*"
            : MergeBypass(environment);
        foreach (string key in ProxyKeys)
            environment.Remove(key);
        if (proxy is not null)
        {
            environment["HTTP_PROXY"] = proxy;
            environment["http_proxy"] = proxy;
            environment["HTTPS_PROXY"] = proxy;
            environment["https_proxy"] = proxy;
        }
        environment["NO_PROXY"] = bypass;
        environment["no_proxy"] = bypass;
        // Node-based agents need this on runtimes with built-in environment-proxy support;
        // older runtimes ignore the variable.
        environment["NODE_USE_ENV_PROXY"] = "1";
    }

    private static Uri AgentDestination(string agent) => agent.ToLowerInvariant() switch
    {
        "claude" => new Uri("https://api.anthropic.com/"),
        "gemini" => new Uri("https://generativelanguage.googleapis.com/"),
        _ => new Uri("https://chatgpt.com/"),
    };

    private static string MergeBypass(IDictionary<string, string?> environment)
    {
        List<string> result = [];
        foreach (string key in new[] { "NO_PROXY", "no_proxy" })
            if (environment.TryGetValue(key, out string? value) && value is not null && !string.IsNullOrWhiteSpace(value))
                foreach (string item in value.Split(','))
                {
                    string host = item.Trim();
                    // Choosing an explicit proxy supersedes an inherited bypass-all setting.
                    if (host.Length > 0 && host != "*" && !result.Contains(host, StringComparer.OrdinalIgnoreCase))
                        result.Add(host);
                }
        foreach (string host in Loopback)
            if (!result.Contains(host, StringComparer.OrdinalIgnoreCase))
                result.Add(host);
        return string.Join(",", result);
    }
}

// Lets the panel distinguish actionable proxy failures from other agent errors without exposing
// proxy credentials or PAC details in the conversation.
internal sealed class AgentProxyException(string message, Exception? inner = null) : Exception(message, inner);
