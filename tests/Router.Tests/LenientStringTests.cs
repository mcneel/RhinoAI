using System.Text.Json;
using NUnit.Framework;

namespace RhinoAI.Router.Tests;

// Covers the scalar-coercion leniency LenientStringConverter adds to string
// binding: some MCP hosts send a string-typed arg as a JSON number or bool, and
// the SDK's default binding rejects that before the tool body ever runs.
[TestFixture]
public class LenientStringTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new LenientStringConverter() },
    };

    private static string? Read(string json) => JsonSerializer.Deserialize<string>(json, Options);

    private sealed class Args
    {
        public string? Name { get; set; }
    }

    [Test]
    public void Non_scalar_token_is_rejected()
    {
        Assert.Throws<JsonException>((Action)(() => { _ = Read("{}"); }));
    }

    [TestCase("{\"Name\":7}", "7")]
    [TestCase("{\"Name\":7.4}", "7.4")]
    [TestCase("{\"Name\":true}", "true")]
    [TestCase("{\"Name\":\"x\"}", "x")]
    [TestCase("{\"Name\":null}", null)]
    public void InputsConvertedToStrings(string json, string? expected)
    {
        Assert.That(JsonSerializer.Deserialize<Args>(json, Options)!.Name, Is.EqualTo(expected));
    }
}
