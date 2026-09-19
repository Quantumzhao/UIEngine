namespace UIEngine.Core;

/// <summary>Identifies live operations whose execution context is controlled by the host.</summary>
public enum InteractionDispatchOperation
{
    DESCRIPTOR_DISCOVERY,
    SUMMARY_READ,
    VALUE_READ,
    VALUE_WRITE,
    REFERENCE_READ,
    COLLECTION_READ,
    ACTION_INVOKE,
}

/// <summary>
/// Lets a descriptor provider explicitly identify operations that are safe outside the host
/// dispatcher. Operations are dispatched when a provider does not implement this contract.
/// </summary>
public interface IInteractionDispatchPolicy
{
    bool CanExecuteDirectly(InteractionDispatchOperation operation);
}
