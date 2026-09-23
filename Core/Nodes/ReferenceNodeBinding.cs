namespace UIEngine.Core;

internal sealed class ReferenceNodeBinding(
    UIEngineHost host,
    Guid owner,
    string id,
    Type referenceType,
    Func<object, object?> read)
{
    public UIEngineHost Host { get; } = host;

    public string Id { get; } = id;

    public Type ReferenceType { get; } = referenceType;

    public InteractionResult<Guid?> Read() => Host.Execute(
        $"read reference {Id}",
        () =>
        {
            var target = Host.ResolveTarget(owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<Guid?>(target.Error!);
            }

            var value = read(target.Value);
            if (value is null)
            {
                return InteractionResult.Success<Guid?>(null);
            }

            if (value.GetType().IsValueType)
            {
                return InteractionResult.Failure<Guid?>(
                    InteractionErrorCode.TYPE_MISMATCH,
                    $"Reference '{Id}' returned a value type.");
            }

            var encountered = Host.GetOrCreateHandle(value);
            var indexed = Host.GetDomainIdentity(encountered);
            return indexed.IsSuccess
                ? InteractionResult.Success<Guid?>(encountered)
                : InteractionResult.Failure<Guid?>(indexed.Error!);
        });
}
