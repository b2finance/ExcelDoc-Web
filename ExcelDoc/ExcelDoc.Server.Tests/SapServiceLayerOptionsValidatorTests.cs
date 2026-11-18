using ExcelDoc.Server.Options;

namespace ExcelDoc.Server.Tests;

public sealed class SapServiceLayerOptionsValidatorTests
{
    private readonly SapServiceLayerOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsDefaultTimeout()
    {
        var options = new SapServiceLayerOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void Validate_RejectsUnsafeTimeout(int timeoutSeconds)
    {
        var options = new SapServiceLayerOptions();
        options.RequestTimeoutSeconds = timeoutSeconds;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("RequestTimeoutSeconds"));
    }
}
