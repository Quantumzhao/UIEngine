using System.Reflection;
using UIEngine.Attributes;

namespace UIEngine.Reflection;

internal static class ReflectionExposure
{
    private static readonly Type[] _EXPOSURE_ATTRIBUTE_TYPES =
    [
        typeof(ExposeAttribute),
        typeof(ActionAttribute),
        typeof(ChildrenAttribute),
        typeof(SummaryAttribute),
    ];

    public static IReadOnlyList<MemberInfo> Discover(Type objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);

        return objectType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Where(_IsExplicitlyExposed)
            .ToArray();
    }

    private static bool _IsExplicitlyExposed(MemberInfo member) =>
        _EXPOSURE_ATTRIBUTE_TYPES.Any(attributeType => member.IsDefined(attributeType, inherit: true));
}
