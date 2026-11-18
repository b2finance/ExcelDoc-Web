using Microsoft.Extensions.Options;

namespace ExcelDoc.Server.Options;

public sealed class SapServiceLayerOptionsValidator : IValidateOptions<SapServiceLayerOptions>
{
    public ValidateOptionsResult Validate(string? name, SapServiceLayerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RequestTimeoutSeconds is < 1 or > 600)
        {
            return ValidateOptionsResult.Fail(
                $"{SapServiceLayerOptions.SectionName}:RequestTimeoutSeconds must be between 1 and 600.");
        }

        return ValidateOptionsResult.Success;
    }
}
