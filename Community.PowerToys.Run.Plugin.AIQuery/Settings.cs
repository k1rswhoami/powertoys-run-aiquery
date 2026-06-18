namespace Community.PowerToys.Run.Plugin.AIQuery;

public enum AIProvider
{
    Claude,
    OpenAICompatible,
}

public class PluginSettings
{
    public AIProvider Provider { get; set; } = AIProvider.Claude;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int TimeoutSeconds { get; set; } = 15;
    public bool GlobalMode { get; set; } = false;
    public int MaxResultChars { get; set; } = 200;
}
