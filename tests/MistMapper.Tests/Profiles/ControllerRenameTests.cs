using MistMapper.Host.Services;
using MistMapper.Shared;

namespace MistMapper.Tests.Profiles;

public sealed class ControllerRenameTests : IDisposable
{
    readonly string _tempDir;
    readonly ProfileService _profiles;

    public ControllerRenameTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mist-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _profiles = new ProfileService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SetControllerSlotDisplayName_persists_custom_label()
    {
        _profiles.EnsureControllerSlot("pad-a", "Steam Controller 2", "sc2");
        _profiles.SetControllerSlotDisplayName("pad-a", "Couch Left");

        var slot = _profiles.FindControllerSlot("pad-a");
        slot!.DisplayName.Should().Be("Couch Left");

        // Reconnect metadata must not overwrite a custom name.
        _profiles.EnsureControllerSlot("pad-a", "Steam Controller 2", "sc2");
        _profiles.FindControllerSlot("pad-a")!.DisplayName.Should().Be("Couch Left");
    }

    [Fact]
    public void SetControllerSlotDisplayName_empty_clears_custom_label()
    {
        _profiles.EnsureControllerSlot("pad-a", "Steam Controller 2", "sc2");
        _profiles.SetControllerSlotDisplayName("pad-a", "P1");
        _profiles.SetControllerSlotDisplayName("pad-a", "  ");

        _profiles.FindControllerSlot("pad-a")!.DisplayName.Should().BeNull();
    }
}
