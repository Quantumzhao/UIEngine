using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace UIEngine.Core;

/// <summary>Applies normalized metadata validation after frontend value conversion.</summary>
internal static class ValueValidation
{
    public static IReadOnlyList<InteractionIssue> Validate(
        object? value,
        bool isNullable,
        IReadOnlyList<SelectionOption> options,
        ValueRange? range,
        IReadOnlyList<ValidationAttribute> attributes,
        object? validationInstance,
        InteractionIssueTarget target,
        string targetId)
    {
        var issues = new List<InteractionIssue>();
        if (value is null && !isNullable &&
            attributes.All(static attribute => attribute is not RequiredAttribute))
        {
            issues.Add(new InteractionIssue(
                InteractionIssueCode.NULL_NOT_ALLOWED,
                target,
                targetId,
                $"'{targetId}' does not allow null."));
        }

        if (value is not null && options.Count > 0 &&
            !options.Any(option => Equals(option.Value, value)))
        {
            issues.Add(new InteractionIssue(
                InteractionIssueCode.NOT_IN_SELECTION,
                target,
                targetId,
                $"'{targetId}' must be one of its declared options."));
        }

        if (value is not null && range is not null && !_IsInRange(value, range))
        {
            issues.Add(new InteractionIssue(
                InteractionIssueCode.OUT_OF_RANGE,
                target,
                targetId,
                $"'{targetId}' must be between {range.Minimum} and {range.Maximum}."));
        }

        foreach (var attribute in attributes)
        {
            var context = new ValidationContext(validationInstance ?? new object())
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
                issues.Add(new InteractionIssue(
                    InteractionIssueCode.RULE_FAILED,
                    target,
                    targetId,
                    $"Validation rule '{attribute.GetType().Name}' failed for '{targetId}'."));
                continue;
            }
            if (result == ValidationResult.Success)
            {
                continue;
            }

            issues.Add(new InteractionIssue(
                attribute is RequiredAttribute
                    ? InteractionIssueCode.REQUIRED
                    : attribute is RangeAttribute
                        ? InteractionIssueCode.OUT_OF_RANGE
                        : InteractionIssueCode.RULE_FAILED,
                target,
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
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))
                .ToArray()
            : [];
    }

    private static bool _IsInRange(object value, ValueRange range)
    {
        try
        {
            return ((IComparable)value).CompareTo(range.Minimum) >= 0 &&
                ((IComparable)value).CompareTo(range.Maximum) <= 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }
}
