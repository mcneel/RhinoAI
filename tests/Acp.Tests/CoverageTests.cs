using System.Reflection;
using System.Text;
using System.Text.Json;
using Acp;

namespace Acp.Tests;

[TestFixture]
public sealed class CoverageTests
{
    [Test]
    public void Generated_constants_and_interfaces_cover_every_method_in_meta()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "meta.json");
        using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(path));

        Assert.That(ProtocolConstants.Version, Is.EqualTo(meta.RootElement.GetProperty("version").GetInt32()));

        AssertSide(meta.RootElement.GetProperty("agentMethods"), typeof(AgentMethods), typeof(IAcpAgent));
        AssertSide(meta.RootElement.GetProperty("clientMethods"), typeof(ClientMethods), typeof(IAcpClient));
    }

    private static void AssertSide(JsonElement methods, Type constants, Type iface)
    {
        HashSet<string> consts = constants
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        foreach (JsonProperty m in methods.EnumerateObject())
        {
            string methodPath = m.Value.GetString()!;
            Assert.That(consts, Does.Contain(methodPath), $"no method constant for '{methodPath}'");
            Assert.That(iface.GetMethod(Pascal(methodPath) + "Async"), Is.Not.Null,
                $"{iface.Name} is missing a method for '{methodPath}'");
        }
    }

    private static string Pascal(string s)
    {
        StringBuilder sb = new();
        bool upper = true;
        foreach (char c in s)
        {
            if (c is '_' or '/' or '-' or '.' or ' ') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }
}
