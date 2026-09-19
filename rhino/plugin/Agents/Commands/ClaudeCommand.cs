namespace Rhino.AI;

[Rhino.Commands.CommandStyle(Rhino.Commands.Style.Hidden)]
public sealed class ClaudeCommand : AgentCommand
{
    public override string EnglishName => "Claude";

    private protected override string AgentName => "claude";
}
