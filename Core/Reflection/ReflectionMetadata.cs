using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using UIEngine.Core.Attributes;

namespace UIEngine.Core;

internal enum ReflectedMemberKind
{
    VALUE,
    REFERENCE,
    COLLECTION,
    ACTION,
}

internal sealed record ReflectedMember(
    string Name,
    ReflectedMemberKind Kind,
    MemberInfo Member,
    Type ValueType,
    bool IsNullable,
    IReadOnlyList<SelectionOption> Options,
    IValueRange? Range,
    IReadOnlyList<ValidationAttribute> ValidationAttributes);

internal sealed record ReflectedType(
    IReadOnlyList<ReflectedMember> Members,
    MemberInfo? SummaryMember);

internal static class ReflectionMetadata
{
    private static readonly ConcurrentDictionary<Type, ReflectedType> _CACHE = new();
    private static readonly NullabilityInfoContext _NULLABILITY = new();

    public static ReflectedType Get(Type type) => _CACHE.GetOrAdd(type, _Build);

    public static bool CanRead(MemberInfo member) => member switch
    {
        PropertyInfo property => property.GetMethod?.IsPublic == true &&
            property.GetIndexParameters().Length == 0,
        FieldInfo => true,
        _ => false,
    };

    public static bool CanWrite(MemberInfo member) => member switch
    {
        PropertyInfo property => property.SetMethod?.IsPublic == true &&
            property.GetIndexParameters().Length == 0 &&
            !property.SetMethod.ReturnParameter
                .GetRequiredCustomModifiers()
                .Contains(typeof(IsExternalInit)),
        FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
        _ => false,
    };

    public static object? Read(MemberInfo member, object instance) => member switch
    {
        PropertyInfo property when property.GetIndexParameters().Length == 0 =>
            property.GetValue(instance),
        FieldInfo field => field.GetValue(instance),
        MethodInfo method when method.GetParameters().Length == 0 => method.Invoke(instance, null),
        _ => throw new InvalidOperationException(
            $"Member '{member.Name}' cannot be read without arguments."),
    };

    public static void Write(MemberInfo member, object instance, object? value)
    {
        switch (member)
        {
            case PropertyInfo property when CanWrite(property):
                property.SetValue(instance, value);
                break;
            case FieldInfo field when CanWrite(field):
                field.SetValue(instance, value);
                break;
            default:
                throw new InvalidOperationException($"Member '{member.Name}' is read-only.");
        }
    }

    public static bool IsNullable(ParameterInfo parameter)
    {
        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        return !parameter.ParameterType.IsValueType &&
            _NULLABILITY.Create(parameter).WriteState != NullabilityState.NotNull;
    }

    public static ValidationAttribute[] GetValidationAttributes(ICustomAttributeProvider provider) =>
        provider.GetCustomAttributes(typeof(ValidationAttribute), inherit: true)
            .Cast<ValidationAttribute>()
            .ToArray();

    private static ReflectedType _Build(Type type)
    {
        const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public;
        var candidates = type.GetMembers(FLAGS)
            .Where(static member =>
                member.IsDefined(typeof(ExposeAttribute), inherit: true) ||
                member.IsDefined(typeof(ChildrenAttribute), inherit: true) ||
                member.IsDefined(typeof(ActionAttribute), inherit: true))
            .OrderBy(static member => member.Name, StringComparer.Ordinal)
            .ThenBy(_Signature, StringComparer.Ordinal)
            .ToArray();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<ReflectedMember>();
        foreach (var member in candidates)
        {
            var memberType = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                MethodInfo method when method.IsDefined(typeof(ActionAttribute), inherit: true) =>
                    method.ReturnType,
                _ => null,
            };
            if (memberType is null)
            {
                continue;
            }

            var name = member.Name;
            for (var suffix = 2; !usedNames.Add(name); suffix++)
            {
                name = $"{member.Name}#{suffix}";
            }

            var kind = member is MethodInfo
                ? ReflectedMemberKind.ACTION
                : _IsCollection(memberType)
                    ? ReflectedMemberKind.COLLECTION
                    : _IsScalar(memberType)
                        ? ReflectedMemberKind.VALUE
                        : ReflectedMemberKind.REFERENCE;
            var validation = GetValidationAttributes(member);
            var options = ValueConversion.GetEnumOptions(memberType);
            var range = validation.OfType<RangeAttribute>()
                .Select(static attribute =>
                    new ValueRange<object>(attribute.Minimum, attribute.Maximum))
                .FirstOrDefault();
            members.Add(new ReflectedMember(
                name,
                kind,
                member,
                memberType,
                _IsNullable(member, memberType) &&
                    validation.All(static attribute => attribute is not RequiredAttribute),
                options,
                range,
                validation));
        }

        var allMembers = type.GetMembers(FLAGS);
        var summary = allMembers.FirstOrDefault(
            static member => member.IsDefined(typeof(SummaryAttribute), inherit: true));
        return new ReflectedType(members, summary);
    }

    private static bool _IsNullable(MemberInfo member, Type memberType)
    {
        if (Nullable.GetUnderlyingType(memberType) is not null)
        {
            return true;
        }

        if (memberType.IsValueType)
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

    private static bool _IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsValueType || underlying == typeof(string);
    }

    private static bool _IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static string _Signature(MemberInfo member) => member switch
    {
        MethodInfo method => string.Join(
            ",",
            method.GetParameters().Select(static parameter => parameter.ParameterType.FullName)),
        PropertyInfo property => property.PropertyType.FullName ?? property.PropertyType.Name,
        FieldInfo field => field.FieldType.FullName ?? field.FieldType.Name,
        _ => string.Empty,
    };
}
