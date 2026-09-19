namespace UIEngine.Core;

/// <summary>Provides an optional stable domain identity for an encountered object.</summary>
public interface IDomainIdentityProvider
{
    bool CanProvideIdentity(Type objectType);

    ValueTask<InteractionResult<string?>> GetIdentityAsync(
        object instance,
        CancellationToken cancellationToken = default);
}
