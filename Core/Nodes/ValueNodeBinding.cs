using System.ComponentModel.DataAnnotations;
using System.Reflection;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

/// <summary>
/// Owns the live host-mediated operations for one scalar node occurrence.
/// </summary>
internal sealed class ValueNodeBinding(
    UIEngineHost host,
    Guid owner,
    string name,
    Type valueType,
    bool canRead,
    bool canWrite,
    bool isNullable,
    IReadOnlyList<SelectionOption> options,
    IValueRange? range,
    IReadOnlyList<ValidationAttribute> validationAttributes,
    Func<object, object?> read,
    Action<object, object?>? write)
{
    private readonly UIEngineHost _Host = host;
    private readonly Guid _Owner = owner;
    private readonly Func<object, object?> _Read = read;
    private readonly Action<object, object?>? _Write = write;
    private readonly IReadOnlyList<ValidationAttribute> _ValidationAttributes = validationAttributes;

    internal UIEngineHost Host { get; } = host;

    public string Name { get; } = name;

    public Type ValueType { get; } = valueType;

    public bool CanRead { get; } = canRead;

    public bool CanWrite { get; } = canWrite;

    public bool IsNullable { get; } = isNullable;

    public IReadOnlyList<SelectionOption> Options { get; } = [.. options];

    public IValueRange? Range { get; } = range;

    public Either<InteractionError, Option<object>> Read() => _Host.Execute<Option<object>>(
        $"read value {Name}",
        () =>
        {
            if (!CanRead)
            {
                return Left(new InteractionError(
                    InteractionErrorCode.UNSUPPORTED,
                    $"Value '{Name}' is not readable."));
            }

            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsRight)
            {
                return Left((InteractionError)target);
            }

            return Right(Optional(_Read(target.IfLeft(
                static error => throw new InvalidOperationException(error.Message)))));
        });

    public Either<InteractionError, Option<object>> Write(object? value) =>
        _Host.Execute<Option<object>>(
        $"write value {Name}",
        () =>
        {
            if (!CanWrite || _Write is null)
            {
                var message = $"Value '{Name}' is read-only.";
                return Left(new InteractionError(
                    InteractionErrorCode.VALIDATION_FAILED,
                    message,
                    [new ValidationIssue(ValidationIssueCode.READ_ONLY, Name, message)]));
            }

            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsRight)
            {
                return Left((InteractionError)target);
            }

            var converted = ValueConversion.Convert(value, ValueType);
            if (!converted.IsRight)
            {
                return Left((InteractionError)converted);
            }

            var convertedValue = ((Option<object>)converted).IfNoneUnsafe((object?)null);
            var issues = ValueConversion.Validate(
                convertedValue,
                IsNullable,
                Options,
                Range,
                _ValidationAttributes,
                target.IfLeft(static error => throw new InvalidOperationException(error.Message)),
                Name);
            if (issues.Count > 0)
            {
                return Left(new InteractionError(
                    InteractionErrorCode.VALIDATION_FAILED,
                    $"Value '{Name}' failed validation.",
                    issues));
            }

            try
            {
                _Write(
                    target.IfLeft(static error => throw new InvalidOperationException(error.Message)),
                    convertedValue);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException is ArgumentException or InvalidOperationException)
            {
                return _SetterRejected(exception.InnerException);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return _SetterRejected(exception);
            }

            return Right(Optional(convertedValue));
        });

    private Either<InteractionError, Option<object>> _SetterRejected(Exception exception) =>
        Left(new InteractionError(
            InteractionErrorCode.VALIDATION_FAILED,
            exception.Message,
            [new ValidationIssue(ValidationIssueCode.RULE_FAILED, Name, exception.Message)]));
}
