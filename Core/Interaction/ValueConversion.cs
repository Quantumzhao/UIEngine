using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace UIEngine.Core;

internal static class ValueConversion
{
    public static InteractionResult<object?> Convert(object? value, Type destinationType)
    {
        var nullableType = Nullable.GetUnderlyingType(destinationType);
        var effectiveType = nullableType ?? destinationType;
        if (value is null)
        {
            return !destinationType.IsValueType || nullableType is not null
                ? InteractionResult.Success<object?>(null)
                : InteractionResult.Failure<object?>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    $"A null value is not valid for '{destinationType.Name}'.");
        }

        if (effectiveType.IsInstanceOfType(value))
        {
            return InteractionResult.Success<object?>(value);
        }

        try
        {
            if (effectiveType == typeof(string))
            {
                return InteractionResult.Success<object?>(
                    System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            if (effectiveType.IsEnum)
            {
                var text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                return text is not null &&
                    Enum.TryParse(effectiveType, text, ignoreCase: true, out var parsed) &&
                    Enum.IsDefined(effectiveType, parsed)
                        ? InteractionResult.Success<object?>(parsed)
                        : _Failure(value, destinationType);
            }

            if (effectiveType == typeof(bool))
            {
                return value is string boolean && bool.TryParse(boolean, out var parsed)
                    ? InteractionResult.Success<object?>(parsed)
                    : _Failure(value, destinationType);
            }

            if (effectiveType == typeof(char))
            {
                var text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                return text?.Length == 1
                    ? InteractionResult.Success<object?>(text[0])
                    : _Failure(value, destinationType);
            }

            if (_IsNumeric(effectiveType))
            {
                return InteractionResult.Success<object?>(System.Convert.ChangeType(
                    value,
                    effectiveType,
                    CultureInfo.InvariantCulture));
            }
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            return _Failure(value, destinationType);
        }

        return _Failure(value, destinationType);
    }

    public static IReadOnlyList<ValidationIssue> Validate(
        object? value,
        bool isNullable,
        IReadOnlyList<SelectionOption> options,
        ValueRange? range,
        IReadOnlyList<ValidationAttribute> attributes,
        object? instance,
        string targetId)
    {
        var issues = new List<ValidationIssue>();
        if (value is null && !isNullable &&
            attributes.All(static attribute => attribute is not RequiredAttribute))
        {
            issues.Add(new ValidationIssue(
                ValidationIssueCode.NULL_NOT_ALLOWED,
                targetId,
                $"'{targetId}' does not allow null."));
        }

        if (value is not null && options.Count > 0 &&
            !options.Any(option => Equals(option.Value, value)))
        {
            issues.Add(new ValidationIssue(
                ValidationIssueCode.NOT_IN_SELECTION,
                targetId,
                $"'{targetId}' must be one of its declared options."));
        }

        if (value is not null && range is not null && !_IsInRange(value, range))
        {
            issues.Add(new ValidationIssue(
                ValidationIssueCode.OUT_OF_RANGE,
                targetId,
                $"'{targetId}' must be between {range.Minimum} and {range.Maximum}."));
        }

        foreach (var attribute in attributes)
        {
            if (attribute is RangeAttribute && range is not null)
            {
                continue;
            }

            var context = new ValidationContext(instance ?? new object())
            {
                MemberName = targetId,
                DisplayName = targetId,
            };
            ValidationResult? result;
            try
            {
                result = attribute.GetValidationResult(value, context);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                issues.Add(new ValidationIssue(
                    ValidationIssueCode.RULE_FAILED,
                    targetId,
                    $"Validation rule '{attribute.GetType().Name}' failed for '{targetId}'."));
                continue;
            }

            if (result == ValidationResult.Success)
            {
                continue;
            }

            issues.Add(new ValidationIssue(
                attribute is RequiredAttribute
                    ? ValidationIssueCode.REQUIRED
                    : attribute is RangeAttribute
                        ? ValidationIssueCode.OUT_OF_RANGE
                        : ValidationIssueCode.RULE_FAILED,
                targetId,
                result?.ErrorMessage ?? $"'{targetId}' did not satisfy its validation rule."));
        }

        return issues;
    }

    public static IReadOnlyList<SelectionOption> GetEnumOptions(Type valueType)
    {
        var enumType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        return enumType.IsEnum
            ? Enum.GetValues(enumType)
                .Cast<object>()
                .Select(static value => new SelectionOption(
                    value,
                    System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))
                .ToArray()
            : [];
    }

    private static bool _IsNumeric(Type type) => Type.GetTypeCode(type) is
        TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or
        TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
        TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static bool _IsInRange(object value, ValueRange range)
    {
        try
        {
            return ((IComparable)value).CompareTo(range.Minimum) >= 0 &&
                ((IComparable)value).CompareTo(range.Maximum) <= 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    private static InteractionResult<object?> _Failure(object value, Type destinationType) =>
        InteractionResult.Failure<object?>(
            InteractionErrorCode.CONVERSION_FAILED,
            $"'{value}' cannot be converted to '{destinationType.Name}'.");
}
