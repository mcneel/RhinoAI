namespace Rhino.AI;

[Rhino.Commands.CommandStyle(Rhino.Commands.Style.Hidden)]
public sealed class CodexCommand : AgentCommand
{
    public override string EnglishName => "Codex";

    private protected override string AgentName => "codex";
}
