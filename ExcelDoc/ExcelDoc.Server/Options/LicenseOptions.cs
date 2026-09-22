namespace ExcelDoc.Server.Options;

public sealed class LicenseOptions
{
    public const string SectionName = "Licensing";
    public string BaseUrl { get; set; } = "https://devhub.b2finance.com/";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
}
