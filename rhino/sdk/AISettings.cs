
using System;

namespace Rhino.AI;

// TODO : Deserialize from somewhere
// TODO : Make not static

/// <summary>
/// Global User Preferences for AI Models, Agents
/// </summary>
public static class UserSettings
{
    
    public static string PreferredModel { get; set; } = string.Empty;

    public static Uri DefaultLocalModelUri { get; set; } = new Uri("http://localhost:1234");

    public static PermissionSet Permissions { get; } = new();

}
