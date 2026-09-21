namespace UIEngine.Core;

/// <summary>Provides an opaque, frontend-safe reference that a host can resolve to a live object.</summary>
public readonly record struct ObjectHandle(Guid Id);
