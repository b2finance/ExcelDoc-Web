namespace ExcelDoc.Server.Options;

public sealed class SapServiceLayerOptions
{
    public const string SectionName = "SapServiceLayer";

    public int RequestTimeoutSeconds { get; set; } = 100;
    
    public int Language { get; set; }
}
