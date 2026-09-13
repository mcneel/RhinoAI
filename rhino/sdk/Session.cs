using System.Collections.Generic;

namespace Rhino.AI;

public class Session
{

    private List<ITurn> PrivateTurns { get; } = [];

    public IReadOnlyList<ITurn> Turns => PrivateTurns;

}
