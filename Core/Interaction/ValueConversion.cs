using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

internal static class ValueConversion
{
    public static Either<InteractionError, Option<object>> Convert(object? value, Type destinationType)
    {
        var nullableType = Nullable.GetUnderlyingType(destinationType);
        var effectiveType = nullableType ?? destinationType;
        if (value is null)
        {
            return !destinationType.IsValueType || nullableType is not null
                ? Right(Option<object>.None)
                : Left(new InteractionError(
                    InteractionErrorCode.VALIDATION_FAILED,
                    $"A null value is not valid for '{destinationType.Name}'."));
        }

        if (effectiveType.IsInstanceOfType(value))
        {
            return Right(Some(value));
        }

        try
        {
            if (effectiveType == typeof(string))
            {
                return Right(Some<object>(
                    System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
            }

            if (effectiveType.IsEnum)
            {
                var text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                return text is not null &&
                    Enum.TryParse(effectiveType, text, ignoreCase: true, out var parsed) &&
                    Enum.IsDefined(effectiveType, parsed)
                        ? Right(Some(parsed))
                        : _Failure(value, destinationType);
            }

            if (effectiveType == typeof(bool))
            {
                return value is string boolean && bool.TryParse(boolean, out var parsed)
                    ? Right(Some<object>(parsed))
                    : _Failure(value, destinationType);
            }

            if (effectiveType == typeof(char))
            {
                var text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                return text?.Length == 1
                    ? Right(Some<object>(text[0]))
                    : _Failure(value, destinationType);
            }

            if (IsNumeric(effectiveType))
            {
                return Right(Some(
                    System.Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture)));
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
        IValueRange? range,
        IReadOnlyList<ValidationAttribute> attributes,
        object? instance,
        string targetName)
    {
        var issues = new List<ValidationIssue>();
        if (value is null && !isNullable &&
            attributes.All(static attribute => attribute is not RequiredAttribute))
        {
            issues.Add(new ValidationIssue(
                ValidationIssueCode.NULL_NOT_ALLOWED,
                targetName,
                $"'{targetName}' does not allow null."));
        }

        if (value is not null && options.Count > 0 &&
            !options.Any(option => Equals(option.Value, value)))
        {
            issues.Add(new ValidationIssue(
                ValidationIssueCode.NOT_IN_SELECTION,
                targetName,
                $"'{targetName}' must be one of its declared options."));
        }

        if (value is not null && range is not null && !_IsInRange(value, range))
        {
            issues.Add(new ValidationIssue(
                ValidationIssueCode.OUT_OF_RANGE,
                targetName,
                $"'{targetName}' must be between {range.Minimum} and {range.Maximum}."));
        }

        foreach (var attribute in attributes)
        {
            if (attribute is RangeAttribute && range is not null)
            {
                continue;
            }

            var context = new ValidationContext(instance ?? new object())
            {
                MemberName = targetName,
                DisplayName = targetName,
            };
            ValidationResult? result;
            try
            {
                result = attribute.GetValidationResult(value, context);
            }
            catch (Exception)
            {
                issues.Add(new ValidationIssue(
                    ValidationIssueCode.RULE_FAILED,
                    targetName,
                    $"Validation rule '{attribute.GetType().Name}' failed for '{targetName}'."));
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
                targetName,
                result?.ErrorMessage ?? $"'{targetName}' did not satisfy its validation rule."));
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

    internal static bool IsNumeric(Type type) => Type.GetTypeCode(type) is
        TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or
        TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
        TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static bool _IsInRange(object value, IValueRange range)
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

    private static Either<InteractionError, Option<object>> _Failure(object value, Type destinationType) =>
        Left(new InteractionError(
            InteractionErrorCode.CONVERSION_FAILED,
            $"'{value}' cannot be converted to '{destinationType.Name}'."));
}
