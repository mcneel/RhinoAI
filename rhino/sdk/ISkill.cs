namespace Rhino.AI;

public interface ISkill
{

    public string Name { get; }

    public string Description { get; }

    public string SkillData { get; }

}

public record struct Skill(string Name, string Description, string SkillData) : ISkill;
