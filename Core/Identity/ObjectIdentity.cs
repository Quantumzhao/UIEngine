namespace UIEngine.Core;

/// <summary>Identifies one runtime object by reference within a single host.</summary>
public readonly record struct ObjectIdentity(Guid RuntimeId);
