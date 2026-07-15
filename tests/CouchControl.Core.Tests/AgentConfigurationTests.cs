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
    }
}
