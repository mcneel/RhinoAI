using System;
using System.Collections.Generic;

namespace Rhino.AI;

// TODO : Deserialize from somewhere
// TODO : Make not

/// <summary>
/// Global User Preferences for AI Models, Agents
/// </summary>
public class UserPreferences
{
    
    public Guid AgentId { get; }
    
    // NOTE : These are Global?
    public PermissionSet Permissions { get; } = new();


    public bool BlockAll { get; set; } = false;

    public HashSet<string> BlockedVendors { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BlockedModels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ApprovedVendors { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ApprovedModels { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a model or vendor is permitted by the user.
    /// </summary>
    /// <returns><c>true</c> if neither the vendor nor the model is blocked, and each approved list the user has filled in contains this vendor or this model. An empty approved list is no opinion, not approve nothing.</returns>
    public bool IsPermitted(string vendor, string model)
    {
        if (BlockAll) return false;
        if (BlockedVendors.Contains(vendor)) return false;
        if (BlockedModels.Contains(model)) return false;

        if (ApprovedVendors.Count > 0 && !ApprovedVendors.Contains(vendor)) return false;
        if (ApprovedModels.Count > 0 && !ApprovedModels.Contains(model)) return false;

        return true;
    }

}
