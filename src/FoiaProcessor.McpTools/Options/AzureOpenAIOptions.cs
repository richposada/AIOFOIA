namespace FoiaProcessor.McpTools.Options;

public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = "gpt-4o-mini";
    public string? ApiKey { get; set; }
}
