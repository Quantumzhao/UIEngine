using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace UIEngine.Core.Reflection;

/// <summary>Normalizes nullable annotations and validation attributes into descriptor metadata.</summary>
internal static class ReflectionValidationMetadata
{
    private static readonly NullabilityInfoContext _NULLABILITY = new();

    public static bool IsNullable(MemberInfo member, Type valueType)
    {
        if (Nullable.GetUnderlyingType(valueType) is not null)
        {
            return true;
        }

        if (valueType.IsValueType)
        {
            return false;
        }

        return member switch
        {
            PropertyInfo property => property.SetMethod is null
                ? _NULLABILITY.Create(property).ReadState != NullabilityState.NotNull
                : _NULLABILITY.Create(property).WriteState != NullabilityState.NotNull,
            FieldInfo field => _NULLABILITY.Create(field).WriteState != NullabilityState.NotNull,
            _ => true,
        };
    }

    public static bool IsNullable(ParameterInfo parameter)
    {
        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        if (parameter.ParameterType.IsValueType)
        {
            return false;
        }

        return _NULLABILITY.Create(parameter).WriteState != NullabilityState.NotNull;
    }

    public static ValidationAttribute[] GetAttributes(ICustomAttributeProvider provider) => provider
        .GetCustomAttributes(typeof(ValidationAttribute), inherit: true)
        .Cast<ValidationAttribute>()
        .ToArray();

    public static ValueRange? GetRange(IEnumerable<ValidationAttribute> attributes)
    {
        var range = attributes.OfType<RangeAttribute>().FirstOrDefault();
        return range is null ? null : new ValueRange(range.Minimum, range.Maximum);
    }

    public static IReadOnlyList<ValidationRuleDescriptor> GetRules(
        IEnumerable<ValidationAttribute> attributes,
        bool hasFiniteSelection)
    {
        var rules = attributes.Select(static attribute => new ValidationRuleDescriptor(
            attribute switch
            {
                RequiredAttribute => ValidationRuleKind.REQUIRED,
                RangeAttribute => ValidationRuleKind.RANGE,
                _ => ValidationRuleKind.PROGRAMMATIC,
            },
            attribute.ErrorMessage));
        return (hasFiniteSelection
                ? rules.Prepend(new ValidationRuleDescriptor(ValidationRuleKind.FINITE_SELECTION))
                : rules)
            .ToArray();
    }
}
