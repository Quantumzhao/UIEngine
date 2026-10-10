using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

internal sealed class ReferenceNodeBinding(
    Guid owner,
    string name,
    Type referenceType,
    Func<object, object?> read)
{
    public string Name { get; } = name;

    public Type ReferenceType { get; } = referenceType;

    public Either<InteractionError, Option<Guid>> Read() => UIEngineHost.Instance.Execute<Option<Guid>>(
        $"read reference {Name}",
        () =>
        {
            var target = UIEngineHost.Instance.ResolveTarget(owner);
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

            return Right(Some(UIEngineHost.Instance.GetOrCreateHandle(value)));
        });
}
