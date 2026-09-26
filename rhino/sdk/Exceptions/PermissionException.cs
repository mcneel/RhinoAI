using System;

namespace Rhino.AI;

/// <summary>
/// A permission exception
/// </summary>
/// <param name="message">The exception reason</param>
public sealed class PermissionException(string message) : Exception(message) { }
