using System.ComponentModel.DataAnnotations;

namespace ExcelDoc.Server.Options;

public sealed class SapServiceLayerOptions
{
    public const string SectionName = "SapServiceLayer";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public int RequestTimeoutSeconds { get; set; } = 100;
    
    public int Language { get; set; }

    public List<SapBaseOptions> Bases { get; set; } = [];
}

public sealed class SapBaseOptions
{
    [Required]
    public string Database { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;
}
