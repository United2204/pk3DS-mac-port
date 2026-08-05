namespace pk3DS.Editors;

/// <summary>
/// Raised when a request cannot be served because of the workspace or the payload.
/// Hosts translate this to a user-facing message; it never signals a bug.
/// </summary>
public sealed class WorkspaceException(string message) : Exception(message);
