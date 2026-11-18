namespace ExcelDoc.Server.Options;

public sealed class LicenseOptions
{
    public const string SectionName = "Licensing";
    public string ProductId { get; set; } = string.Empty;
}
