namespace RhMcp.Router;

public record RouterConfig(string DefaultVersion)
{
    public static RouterConfig FromArgs(string[] args)
    {
        var defaultVersion = "8";

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--default-version" || args[i] == "-v")
            {
                defaultVersion = args[i + 1];
            }
        }

        return new RouterConfig(defaultVersion);
    }
}
