namespace FoiaProcessor.Api.Contracts;

public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
}

public record ComponentHealth(
    string Name,
    string Category,
    HealthStatus Status,
    string? Description,
    long DurationMs,
    IReadOnlyDictionary<string, object?>? Data,
    string? Error);

public record SystemHealthReport(
    HealthStatus Status,
    DateTimeOffset Timestamp,
    IReadOnlyList<ComponentHealth> Components);
