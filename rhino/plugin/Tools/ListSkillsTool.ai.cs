using Rhino.AI.Skills;

namespace Rhino.AI.Tools;

/// <summary>
/// The skills installed plugins ship, for agents outside Rhino, whose prompt has no list of them.
/// </summary>
[McpServerToolType]
internal static class ListSkillsTool
{

    [McpServerTool("list_skills", "List Plugin Skills", true, false)]
    [Description("List the skills installed Rhino plugins ship: how to use a plugin's commands and components for a task, such as building a site model. Call it when a request may need a plugin, then read_skill the one that fits before acting.")]
    [BackgroundThread]
    public static IToolResult ListSkills(RhinoDoc _)
    {
        IReadOnlyList<PlugInSkill> skills = PlugInSkills.Find();
        if (skills.Count == 0)
            return Success(ContentBlock.CreateText("No installed plugin ships a skill."));

        return Success(ContentBlock.CreateText(PlugInSkills.Prompt(skills)!));
    }

}
