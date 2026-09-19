using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using UIEngine.Attributes;

namespace UIEngine.Reflection;

/// <summary>Identifies the semantic descriptor role assigned to a reflected member.</summary>
internal enum ReflectionMemberKind
{
    VALUE,
    REFERENCE,
    COLLECTION,
    ACTION,
}

/// <summary>Stores immutable, type-level metadata for one descriptor member.</summary>
internal sealed record ReflectionMemberMetadata(
    string Id,
    string DisplayName,
    Type MemberType,
    ReflectionMemberKind Kind,
    MemberInfo Member);

/// <summary>Stores cached exposure and descriptor metadata for one reflected type.</summary>
internal sealed record ReflectionTypeMetadata(
    IReadOnlyList<MemberInfo> ExposedMembers,
    IReadOnlyList<ReflectionMemberMetadata> DescriptorMembers,
    MemberInfo? SummaryMember);

/// <summary>Builds reflection metadata once per runtime type and reuses it across instances.</summary>
internal static class ReflectionTypeMetadataCache
{
    private static readonly ConcurrentDictionary<Type, ReflectionTypeMetadata> _CACHE = new();

    public static ReflectionTypeMetadata GetOrCreate(Type objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        return _CACHE.GetOrAdd(objectType, _Build);
    }

    private static ReflectionTypeMetadata _Build(Type objectType)
    {
        const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public;

        var exposedMembers = objectType
            .GetMembers(FLAGS)
            .Where(_IsExplicitlyExposed)
            .OrderBy(static member => member.Name, StringComparer.Ordinal)
            .ThenBy(static member => member.MemberType)
            .ThenBy(_GetStableSignature, StringComparer.Ordinal)
            .ToArray();

        var candidates = exposedMembers
            .Select(_CreateCandidate)
            .OfType<_MemberCandidate>()
            .ToArray();
        var usedIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var descriptorMembers = new List<ReflectionMemberMetadata>(candidates.Length);

        foreach (var candidate in candidates)
        {
            var identifier = candidate.Member.Name;
            var suffix = 2;
            while (!usedIdentifiers.Add(identifier))
            {
                identifier = $"{candidate.Member.Name}#{suffix}";
                suffix++;
            }

            descriptorMembers.Add(new ReflectionMemberMetadata(
                identifier,
                candidate.Member.Name,
                candidate.MemberType,
                candidate.Kind,
                candidate.Member));
        }

        var summaryMember = exposedMembers.FirstOrDefault(
            static member => member.IsDefined(typeof(SummaryAttribute), inherit: true));

        return new ReflectionTypeMetadata(exposedMembers, descriptorMembers, summaryMember);
    }

    private static _MemberCandidate? _CreateCandidate(MemberInfo member)
    {
        if (member is MethodInfo method && method.IsDefined(typeof(ActionAttribute), inherit: true))
        {
            return new _MemberCandidate(member, method.ReturnType, ReflectionMemberKind.ACTION);
        }

        var isDescriptorMember =
            member.IsDefined(typeof(ExposeAttribute), inherit: true) ||
            member.IsDefined(typeof(ChildrenAttribute), inherit: true);
        if (!isDescriptorMember)
        {
            return null;
        }

        var memberType = member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => null,
        };
        if (memberType is null)
        {
            return null;
        }

        var kind = _IsScalar(memberType)
            ? ReflectionMemberKind.VALUE
            : _IsCollection(memberType)
                ? ReflectionMemberKind.COLLECTION
                : ReflectionMemberKind.REFERENCE;

        return new _MemberCandidate(member, memberType, kind);
    }

    private static bool _IsExplicitlyExposed(MemberInfo member) =>
        member.IsDefined(typeof(ExposeAttribute), inherit: true) ||
        member.IsDefined(typeof(ActionAttribute), inherit: true) ||
        member.IsDefined(typeof(ChildrenAttribute), inherit: true) ||
        member.IsDefined(typeof(SummaryAttribute), inherit: true);

    private static bool _IsScalar(Type memberType)
    {
        var underlyingType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        return underlyingType.IsValueType || underlyingType == typeof(string);
    }

    private static bool _IsCollection(Type memberType) =>
        memberType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(memberType);

    private static string _GetStableSignature(MemberInfo member)
    {
        var declaringType = member.DeclaringType?.FullName ?? string.Empty;
        var memberSignature = member switch
        {
            MethodInfo method => $"{method.GetGenericArguments().Length}:" + string.Join(
                ",",
                method.GetParameters().Select(static parameter =>
                    parameter.ParameterType.FullName ?? parameter.ParameterType.Name)),
            PropertyInfo property => property.PropertyType.FullName ?? property.PropertyType.Name,
            FieldInfo field => field.FieldType.FullName ?? field.FieldType.Name,
            _ => string.Empty,
        };
        return $"{declaringType}:{memberSignature}";
    }

    /// <summary>Holds a classified member before its unique identifier is assigned.</summary>
    private sealed record _MemberCandidate(
        MemberInfo Member,
        Type MemberType,
        ReflectionMemberKind Kind);
}
