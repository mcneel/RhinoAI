namespace Rhino.AI.Integration.Tests.Harness;

public static class Args
{
    public static Dictionary<string, object?> Of(params (string Name, object? Value)[] pairs)
    {
        Dictionary<string, object?> args = new(pairs.Length);
        foreach ((string name, object? value) in pairs)
        {
            args.Add(name, value);
        }
        return args;
    }
}
