using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI.Tools;

public class ReadSkill : ITool
{
    public string Name => "read_skill";

    public string Description => "Allows for reading a skill";

    public bool ReadOnly => true;

    public bool Destructive => false;

    public ToolArg[] Args => [new ToolArg("name", "The name of the skill.", ToolArgType.String, true)];

    private Func<string, ISkill?> GetSkillFromKey { get; }

    public ReadSkill(Func<string, ISkill?> value)
    {
        GetSkillFromKey = value;
    }

    public Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!args.TryGetString(Args[0].Name, out string skillName))
            return Task.FromResult(ToolReturn.Failure("name parameter is mandatory", "Call read_skill again with name set to the skill name."));

        ISkill? skill = GetSkillFromKey(skillName);
        if (skill is null)
            return Task.FromResult(ToolReturn.Failure($"No skill named '{skillName}'.", "Check the skill name against the available skills list."));

        return Task.FromResult(ToolReturn.Success(skill.SkillData));
    }

}
