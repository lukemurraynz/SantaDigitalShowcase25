namespace Models;

public class LimitsOptions
{
    public int TopN { get; set; } = 3;
    public int DebounceSeconds { get; set; } = 5;
    public TimeoutsOptions Timeouts { get; set; } = new();
}

public class TimeoutsOptions
{
    public int DefaultMs { get; set; } = 10000;
}

public class ElfRecommendationAgentOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string? SystemPromptOverride { get; set; }
}
