using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

internal sealed class ReferenceNodeBinding(
    UIEngineHost host,
    Guid owner,
    string name,
    Type referenceType,
    Func<object, object?> read)
{
    public UIEngineHost Host { get; } = host;

    public string Name { get; } = name;

    public Type ReferenceType { get; } = referenceType;

    public Either<InteractionError, Option<Guid>> Read() => Host.Execute<Option<Guid>>(
        $"read reference {Name}",
        () =>
        {
            var target = Host.ResolveTarget(owner);
            if (!target.IsRight)
            {
                return Left((InteractionError)target);
            }

            var value = read(target.IfLeft(
                static error => throw new InvalidOperationException(error.Message)));
            if (value is null)
            {
                return Right(Option<Guid>.None);
            }

            if (value.GetType().IsValueType)
            {
                return Left(new InteractionError(
                    InteractionErrorCode.TYPE_MISMATCH,
                    $"Reference '{Name}' returned a value type."));
            }

            return Right(Some(Host.GetOrCreateHandle(value)));
        });
}
