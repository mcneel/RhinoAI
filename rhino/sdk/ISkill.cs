namespace Rhino.AI;

/// <summary>
/// A skill for MCPs and Agents
/// </summary>
public interface ISkill
{

    /// <summary>
    /// The name of the <see cref="ISkill"/>
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// A short description of the <see cref="ISkill"/>
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The full data in the <see cref="ISkill"/> file
    /// </summary>
    public string SkillData { get; }

}

/// <summary>
/// A skill for MCPs and Agents
/// </summary>
/// <param name="Name">The name</param>
/// <param name="Description">The description</param>
/// <param name="SkillData">The full data</param>
public sealed record Skill(string Name, string Description, string SkillData) : ISkill;
