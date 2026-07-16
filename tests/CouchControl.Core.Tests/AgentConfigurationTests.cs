using CouchControl.Core.Models;

namespace CouchControl.Core.Tests;

public sealed class AgentConfigurationTests
{
    [Fact]
    public void DefaultConfiguration_IsValid()
    {
        var configuration = new AgentConfiguration();

        var validation = configuration.Validate();

        Assert.True(validation.Succeeded);
        Assert.Equal("CouchControl Agent", configuration.AgentName);
        Assert.True(configuration.PreferredCouchMode.IsValid);
        Assert.Null(configuration.ApiListeningInterfaceId);
        Assert.Equal(4000, configuration.TvPreparationDelayMs);
    }

    [Fact]
    public void Validate_FailsForOutOfRangeTvPreparationDelay()
    {
        var configuration = new AgentConfiguration
        {
            TvPreparationDelayMs = 60001
        };

        var validation = configuration.Validate();

        Assert.False(validation.Succeeded);
        Assert.Equal("invalid_tv_preparation_delay", validation.ErrorCode);
    }
}
