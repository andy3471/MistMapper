using MistMapper.Host.Services;
using MistMapper.Host.Viiper;

namespace MistMapper.Tests.Fakes;

public sealed class FakeViiperHealth : IViiperHealth
{
    public string DependencyId { get; init; } = "viiper-test";
    public string DisplayName { get; init; } = "VIIPER (test)";
    public bool ProbeOk { get; set; } = true;
    public string ProbeDetail { get; set; } = "Test probe ok";

    public Task<(bool Ok, string Detail)> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult((ProbeOk, ProbeDetail));

    public Task<(bool Ok, string Detail)> EnsureRunningAsync(CancellationToken ct = default) =>
        Task.FromResult((ProbeOk, ProbeDetail));
}
