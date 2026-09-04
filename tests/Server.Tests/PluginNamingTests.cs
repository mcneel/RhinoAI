using NUnit.Framework;
using RhinoAI.ScriptProjects;

namespace RhinoAI.Server.Tests;

[TestFixture]
internal sealed class PluginNamingTests
{
    [TestCase("add", PluginCommandAction.Add)]
    [TestCase("update", PluginCommandAction.Update)]
    [TestCase("delete", PluginCommandAction.Delete)]
    [TestCase("  ADD  ", PluginCommandAction.Add)]
    [TestCase("Delete", PluginCommandAction.Delete)]
    public void TryParseAction_accepts_the_documented_verbs(string input, PluginCommandAction expected)
    {
        Assert.That(PluginNaming.TryParseAction(input, out PluginCommandAction parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("list")]
    [TestCase("remove")]
    [TestCase("create")]
    public void TryParseAction_rejects_anything_else(string? input)
    {
        Assert.That(PluginNaming.TryParseAction(input, out _), Is.False);
    }

    [TestCase("MakeItBlue")]
    [TestCase("make_it_blue")]
    [TestCase("Cmd2")]
    [TestCase("_leading")]
    public void ValidateCommandName_accepts_letters_digits_and_underscores(string name)
    {
        Assert.That(PluginNaming.ValidateCommandName(name), Is.EqualTo(CommandNameProblem.None));
    }

    [TestCase("", CommandNameProblem.Empty)]
    [TestCase("   ", CommandNameProblem.Empty)]
    [TestCase(null, CommandNameProblem.Empty)]
    [TestCase("Make It Blue", CommandNameProblem.InvalidCharacters)]
    [TestCase("Make-It-Blue", CommandNameProblem.InvalidCharacters)]
    [TestCase("Café", CommandNameProblem.InvalidCharacters)]
    [TestCase("Make.It", CommandNameProblem.InvalidCharacters)]
    public void ValidateCommandName_rejects_names_the_generated_class_could_not_use(
        string? name, CommandNameProblem expected)
    {
        Assert.That(PluginNaming.ValidateCommandName(name), Is.EqualTo(expected));
    }

    [Test]
    public void ValidateCommandName_rejects_an_over_long_name()
    {
        Assert.That(
            PluginNaming.ValidateCommandName(new string('A', 65)),
            Is.EqualTo(CommandNameProblem.TooLong));
    }

    [Test]
    public void Describe_refuses_to_describe_a_name_that_is_fine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PluginNaming.Describe(CommandNameProblem.None, "MakeItBlue"));
    }

    [TestCase("callum", "CallumPlugIn")]
    [TestCase("callum.sykes", "CallumSykesPlugIn")]
    [TestCase("Callum Sykes", "CallumSykesPlugIn")]
    [TestCase("DOMAIN\\csykes", "DOMAINCsykesPlugIn")]
    [TestCase("2fast", "P2fastPlugIn")]
    public void PluginNameFor_builds_an_assembly_safe_name(string userName, string expected)
    {
        Assert.That(PluginNaming.PluginNameFor(userName), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("...")]
    [TestCase("日本語")]
    public void PluginNameFor_falls_back_when_the_username_yields_nothing(string? userName)
    {
        Assert.That(PluginNaming.PluginNameFor(userName), Is.EqualTo(PluginNaming.FallbackPluginName));
    }

    [Test]
    public void SanitisePluginName_does_not_append_the_suffix_to_an_override()
    {
        Assert.That(PluginNaming.SanitisePluginName("My Tools"), Is.EqualTo("MyTools"));
    }

    [Test]
    public void SanitisePluginName_falls_back_for_an_unusable_override()
    {
        Assert.That(PluginNaming.SanitisePluginName("!!!"), Is.EqualTo(PluginNaming.FallbackPluginName));
    }
}
