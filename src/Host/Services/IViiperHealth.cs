namespace MistMapper.Host.Services;

public interface IViiperHealth
{
    string DependencyId { get; }
    string DisplayName { get; }
    Task<(bool Ok, string Detail)> ProbeAsync(CancellationToken ct = default);
    Task<(bool Ok, string Detail)> EnsureRunningAsync(CancellationToken ct = default);
}
