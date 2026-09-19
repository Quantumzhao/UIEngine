using System.Globalization;
using UIEngine.Core;

namespace UIEngine.Reflection;

/// <summary>Converts frontend values into the scalar types supported by reflection operations.</summary>
internal static class ReflectionValueConverter
{
    public static InteractionResult<object?> Convert(object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

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
                var enumText = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                if (enumText is not null &&
                    Enum.TryParse(effectiveType, enumText, ignoreCase: true, out var enumValue) &&
                    Enum.IsDefined(effectiveType, enumValue))
                {
                    return InteractionResult.Success<object?>(enumValue);
                }

                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.CONVERSION_FAILED,
                    $"'{value}' is not a defined {effectiveType.Name} value.");
            }

            if (effectiveType == typeof(bool))
            {
                if (value is string boolText && bool.TryParse(boolText, out var boolValue))
                {
                    return InteractionResult.Success<object?>(boolValue);
                }

                if (value is bool directBool)
                {
                    return InteractionResult.Success<object?>(directBool);
                }

                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.CONVERSION_FAILED,
                    $"'{value}' is not a Boolean value.");
            }

            if (effectiveType == typeof(char))
            {
                var characterText = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                return characterText?.Length == 1
                    ? InteractionResult.Success<object?>(characterText[0])
                    : InteractionResult.Failure<object?>(
                        InteractionErrorCode.CONVERSION_FAILED,
                        $"'{value}' is not a single character.");
            }

            if (_IsNumeric(effectiveType))
            {
                return InteractionResult.Success<object?>(System.Convert.ChangeType(
                    value,
                    effectiveType,
                    CultureInfo.InvariantCulture));
            }

            return InteractionResult.Failure<object?>(
                InteractionErrorCode.CONVERSION_FAILED,
                $"Conversion to '{destinationType.Name}' is not supported.");
        }
        catch (FormatException)
        {
            return _ConversionFailure(value, destinationType);
        }
        catch (InvalidCastException)
        {
            return _ConversionFailure(value, destinationType);
        }
        catch (OverflowException)
        {
            return _ConversionFailure(value, destinationType);
        }
    }

    private static bool _IsNumeric(Type type) => Type.GetTypeCode(type) is
        TypeCode.SByte or
        TypeCode.Byte or
        TypeCode.Int16 or
        TypeCode.UInt16 or
        TypeCode.Int32 or
        TypeCode.UInt32 or
        TypeCode.Int64 or
        TypeCode.UInt64 or
        TypeCode.Single or
        TypeCode.Double or
        TypeCode.Decimal;

    private static InteractionResult<object?> _ConversionFailure(object value, Type destinationType) =>
        InteractionResult.Failure<object?>(
            InteractionErrorCode.CONVERSION_FAILED,
            $"'{value}' cannot be converted to '{destinationType.Name}'.");
}
