using MistMapper.Host.Services;
using MistMapper.Shared;

namespace MistMapper.Tests.Profiles;

public sealed class ProfileServiceTests : IDisposable
{
    readonly string _tempDir;
    readonly ProfileService _service;

    public ProfileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "scb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new ProfileService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ResolveForGame_returns_manual_when_no_binding_matches()
    {
        var active = _service.ActiveProfile;

        var (profile, source) = _service.ResolveForGame("notepad.exe", @"C:\Windows\System32\notepad.exe");

        source.Should().Be(ActiveProfileSource.Manual);
        profile.Id.Should().Be(active.Id);
    }

    [Fact]
    public void ResolveForGame_matches_exe_binding()
    {
        var profile = _service.CreateFromLayout(OfficialLayouts.Gamepad, "For Notepad");
        _service.BindToGame(profile.Id, "notepad.exe");

        var (resolved, source) = _service.ResolveForGame("notepad.exe", @"C:\Windows\System32\notepad.exe");

        source.Should().Be(ActiveProfileSource.GameRule);
        resolved.Id.Should().Be(profile.Id);
    }

    [Fact]
    public void ResolveForGame_path_contains_beats_exe_only()
    {
        var exeProfile = _service.CreateFromLayout(OfficialLayouts.Desktop, "Any game");
        var pathProfile = _service.CreateFromLayout(OfficialLayouts.Racing, "Steam game");
        _service.BindToGame(exeProfile.Id, "game.exe");
        _service.BindToGame(pathProfile.Id, "game.exe", matchPathContains: @"steamapps\common");

        var path = @"D:\Steam\steamapps\common\MyGame\game.exe";
        var (resolved, source) = _service.ResolveForGame("game.exe", path);

        source.Should().Be(ActiveProfileSource.GameRule);
        resolved.Id.Should().Be(pathProfile.Id);
    }

    [Fact]
    public void RemapAction_locked_guide_throws()
    {
        var profile = _service.ActiveProfile;

        var act = () => _service.RemapAction(profile.Id, "Steam", OutputAction.FromXbox(XboxOutput.A));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*locked*");
    }

    [Fact]
    public void Duplicate_creates_unique_name_when_not_provided()
    {
        var src = _service.ActiveProfile;
        var copy = _service.Duplicate(src.Id);

        copy.Id.Should().NotBe(src.Id);
        copy.Name.Should().StartWith(src.Name);
        copy.Name.Should().NotBe(src.Name);
    }
}
