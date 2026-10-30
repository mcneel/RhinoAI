using Rhino.AI.Skills;

namespace Rhino.AI.Tools;

/// <summary>
/// A plugin's skill in full. Named, described and answered as the SDK's read_skill, so every
/// agent loads a skill the same way.
/// </summary>
[McpServerToolType]
internal static class ReadSkillTool
{

    [McpServerTool("read_skill", "Read Plugin Skill", true, false)]
    [Description("Allows for reading a skill")]
    [BackgroundThread]
    public static IToolResult ReadSkill(
        RhinoDoc _,
        [Description("The name of the skill.")] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Failure(ToolError.BadArgument, "name parameter is mandatory", "Call read_skill again with name set to the skill name.");

        PlugInSkill? skill = PlugInSkills.Get(name.Trim());
        if (skill is null)
            return Failure(ToolError.NotFound, $"No skill named '{name}'.", "Check the skill name against the available skills list.");

        return Success(ContentBlock.CreateText(skill.SkillData));
    }

}
