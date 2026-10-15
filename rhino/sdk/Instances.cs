
namespace Rhino.AI;

/// <summary>
/// All of the static instances
/// </summary>
internal static class Instances
{

    public static Agents Agents { get; internal set; } = new ();

    public static UserPreferences Preferences { get; internal set; } = new();

}
