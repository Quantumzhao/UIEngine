using Xunit;

namespace UIEngine.Legacy.Characterization.Tests;

[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class LegacyDashboardTestGroup : ICollectionFixture<LegacyDashboardFixture>
{
    public const string NAME = "Legacy Dashboard";
}
