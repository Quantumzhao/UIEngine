using System.Reflection;

namespace UIEngine.Reflection;

internal static class ReflectionExposure
{
    public static IReadOnlyList<MemberInfo> Discover(Type objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        return ReflectionTypeMetadataCache.GetOrCreate(objectType).ExposedMembers;
    }
}
