using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Tools;

/// <summary>
/// An MCP tool to read a skill and load its entirety into the context
/// </summary>
public sealed record ReadSkill : Tool
{

    private Func<string, ISkill?> GetSkillFromKey { get; }

    public ReadSkill(Func<string, ISkill?> value) : base("read_skill",
                                "Allows for reading a skill",
                                true,
                                false,
                                [
                                    new ("name", "The name of the skill.", ToolArgType.String, true),
                                ])
    {
        GetSkillFromKey = value;
    }

    public override Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetString(Args[0].Name, out string skillName))
            return Task.FromResult(ToolReturn.Failure("name parameter is mandatory", "Call read_skill again with name set to the skill name."));

        ISkill? skill = GetSkillFromKey(skillName);
        if (skill is null)
            return Task.FromResult(ToolReturn.Failure($"No skill named '{skillName}'.", "Check the skill name against the available skills list."));

        return Task.FromResult(ToolReturn.Success(skill.SkillData));
    }

}
