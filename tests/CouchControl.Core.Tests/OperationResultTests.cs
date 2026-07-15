using CouchControl.Core.Models;

namespace CouchControl.Core.Tests;

public sealed class OperationResultTests
{
    [Fact]
    public void SuccessFactory_CreatesSuccessfulResult()
    {
        var result = OperationResult.Success("done");

        Assert.True(result.Succeeded);
        Assert.Equal("done", result.Message);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void FailureFactory_CreatesFailedResult()
    {
        var result = OperationResult.Failure("broken", "error_code");

        Assert.False(result.Succeeded);
        Assert.Equal("broken", result.Message);
        Assert.Equal("error_code", result.ErrorCode);
    }

    [Fact]
    public void PartialSuccessFactory_CreatesSuccessfulPartialResult()
    {
        var result = OperationResult.PartialSuccess("partial", outcome: "Partial success");

        Assert.True(result.Succeeded);
        Assert.True(result.IsPartialSuccess);
        Assert.Equal("Partial success", result.Outcome);
    }
}
