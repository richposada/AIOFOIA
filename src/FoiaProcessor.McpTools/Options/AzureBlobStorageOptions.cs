namespace FoiaProcessor.McpTools.Options;

public class AzureBlobStorageOptions
{
    public const string SectionName = "AzureBlobStorage";

    public string? ConnectionString { get; set; }
    public string? AccountName { get; set; }
    public string ContainerName { get; set; } = "foia-releases";
    public int SasExpirationDays { get; set; } = 7;
}
