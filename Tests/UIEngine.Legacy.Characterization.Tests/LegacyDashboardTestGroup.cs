using Xunit;

namespace UIEngine.Legacy.Characterization.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LegacyDashboardTestGroup : ICollectionFixture<LegacyDashboardFixture>
{
    public const string Name = "Legacy Dashboard";
}
