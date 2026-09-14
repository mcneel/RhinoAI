using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public interface ISkill
{

    public string Name { get; }
    
    public string SkillData { get; }

}

public record struct Skill(string Name, string SkillData);
