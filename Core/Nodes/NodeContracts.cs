namespace UIEngine.Core;

/// <summary>One frontend-neutral occurrence of an exposed object or member.</summary>
public abstract class ObjectNode
{
    internal ObjectNode(UIEngineHost host, string id, Type valueType)
    {
        Host = host;
        Id = id;
        ValueType = valueType;
    }

    /// <summary>The root, member, or selected-entry identifier for this occurrence.</summary>
    public string Id { get; }

    /// <summary>The .NET type represented by this node.</summary>
    public Type ValueType { get; }

    /// <summary>Whether this node has no language-semantic navigation edge.</summary>
    public bool IsTerminal => this is not INavigableNode;

    internal UIEngineHost Host { get; }
}

/// <summary>Marks a node that exposes one or more language-semantic navigation edges.</summary>
public interface INavigableNode;

/// <summary>Exposes one live object and its opted-in members.</summary>
public interface IObjectNode : INavigableNode
{
    ObjectHandle Handle { get; }

    DomainIdentity? DomainIdentity { get; }

    string? Summary { get; }

    IReadOnlyList<ObjectNode> Members { get; }
}

/// <summary>Identifies a node backed by a reflected member.</summary>
public interface IMemberNode
{
    Type DeclaringType { get; }
}

/// <summary>Identifies a node backed by a reflected property.</summary>
public interface IPropertyNode : IMemberNode;

/// <summary>Identifies a node backed by a reflected field.</summary>
public interface IFieldNode : IMemberNode;

/// <summary>
/// Identifies a scalar supplied by <see cref="TypeExposure"/> rather than a reflected member.
/// Such a node does not also implement <see cref="IPropertyNode"/> or <see cref="IFieldNode"/>.
/// </summary>
public interface IProgrammaticValueNode;

/// <summary>Exposes the current target of a reference-valued node.</summary>
public interface IReferenceNode : INavigableNode
{
    Type ReferenceType { get; }

    Task<InteractionResult<ObjectHandle?>> ReadReferenceAsync();
}

/// <summary>Identifies a node with a readable scalar value.</summary>
public interface IReadableValueNode
{
    Task<InteractionResult<object?>> ReadValueAsync();
}

/// <summary>Identifies a node with a writable scalar value.</summary>
public interface IWritableValueNode
{
    IReadOnlyList<SelectionOption> Options { get; }

    ValueRange? Range { get; }

    Task<InteractionResult<object?>> WriteValueAsync(object? value);
}

/// <summary>Identifies a value node that accepts <see langword="null"/>.</summary>
public interface INullableValueNode;

/// <summary>Identifies string value semantics.</summary>
public interface IStringNode;

/// <summary>Identifies character value semantics.</summary>
public interface ICharacterNode;

/// <summary>Identifies Boolean value semantics.</summary>
public interface IBooleanNode;

/// <summary>Identifies numeric value semantics.</summary>
public interface INumberNode;

/// <summary>Identifies enum value semantics and the enum's closed set of options.</summary>
public interface IEnumNode
{
    IReadOnlyList<SelectionOption> Options { get; }
}

/// <summary>Exposes bounded windows from a collection-valued node.</summary>
public interface ICollectionNode : INavigableNode
{
    Type ElementType { get; }

    Type? KeyType { get; }

    Task<InteractionResult<CollectionSlice>> ReadEntriesAsync(long offset, int limit);
}

/// <summary>Describes one user-supplied method parameter.</summary>
public sealed record MethodParameter(
    string Id,
    Type ParameterType,
    bool IsRequired,
    bool IsNullable,
    bool HasDefaultValue,
    object? DefaultValue,
    IReadOnlyList<SelectionOption> Options,
    ValueRange? Range);

/// <summary>Exposes reflected method metadata and starts host-owned invocations.</summary>
public interface IMethodNode : IMemberNode
{
    IReadOnlyList<MethodParameter> Parameters { get; }

    Type? ResultType { get; }

    bool IsAsynchronous { get; }

    Type? ProgressType { get; }

    bool SupportsCancellation { get; }

    Task<InteractionResult<ActionInvocation>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments);
}
