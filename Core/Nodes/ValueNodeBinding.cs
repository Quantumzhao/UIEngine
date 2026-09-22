using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace UIEngine.Core;

/// <summary>
/// Owns the live host-mediated operations for one scalar exposure. Descriptor adapters delegate
/// here so node occurrences can reuse the same implementation as the migration proceeds.
/// </summary>
internal sealed class ValueNodeBinding(
    UIEngineHost host,
    ObjectHandle owner,
    string id,
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
    private readonly ObjectHandle _Owner = owner;
    private readonly Func<object, object?> _Read = read;
    private readonly Action<object, object?>? _Write = write;
    private readonly IReadOnlyList<ValidationAttribute> _ValidationAttributes = validationAttributes;

    internal UIEngineHost Host { get; } = host;

    public string Id { get; } = id;

    public Type ValueType { get; } = valueType;

    public bool CanRead { get; } = canRead;

    public bool CanWrite { get; } = canWrite;

    public bool IsNullable { get; } = isNullable;

    public IReadOnlyList<SelectionOption> Options { get; } = [.. options];

    public IValueRange? Range { get; } = range;

    public Task<InteractionResult<object?>> ReadAsync() => _Host.ExecuteAsync(
        $"read value {Id}",
        async () =>
        {
            if (!CanRead)
            {
                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.UNSUPPORTED,
                    $"Value '{Id}' is not readable.");
            }

            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<object?>(target.Error!);
            }

            await Task.CompletedTask;
            return InteractionResult.Success(_Read(target.Value));
        });

    public Task<InteractionResult<object?>> WriteAsync(object? value) => _Host.ExecuteAsync(
        $"write value {Id}",
        async () =>
        {
            if (!CanWrite || _Write is null)
            {
                var message = $"Value '{Id}' is read-only.";
                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    message,
                    [new ValidationIssue(ValidationIssueCode.READ_ONLY, Id, message)]);
            }

            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<object?>(target.Error!);
            }

            var converted = ValueConversion.Convert(value, ValueType);
            if (!converted.IsSuccess)
            {
                return InteractionResult.Failure<object?>(converted.Error!);
            }

            var issues = ValueConversion.Validate(
                converted.Value,
                IsNullable,
                Options,
                Range,
                _ValidationAttributes,
                target.Value,
                Id);
            if (issues.Count > 0)
            {
                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    $"Value '{Id}' failed validation.",
                    issues);
            }

            try
            {
                _Write(target.Value, converted.Value);
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

            await Task.CompletedTask;
            return InteractionResult.Success(converted.Value);
        });

    private InteractionResult<object?> _SetterRejected(Exception exception) =>
        InteractionResult.Failure<object?>(
            InteractionErrorCode.VALIDATION_FAILED,
            exception.Message,
            [new ValidationIssue(ValidationIssueCode.RULE_FAILED, Id, exception.Message)]);
}
