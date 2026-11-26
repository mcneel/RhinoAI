using System;

namespace Rhino.AI;

internal sealed record PlugInPermissionEntry(Guid Id, string Name, bool Enabled);
