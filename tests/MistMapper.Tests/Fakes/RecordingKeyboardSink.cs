using MistMapper.Host.Mapping;
using MistMapper.Shared;

namespace MistMapper.Tests.Fakes;

public sealed class RecordingKeyboardSink : IKeyboardSink
{
    public List<(int Vk, bool Down)> Keys { get; } = [];
    public List<(KeyModifiers Mods, bool Down)> Modifiers { get; } = [];

    public void SetKey(int virtualKey, bool down) => Keys.Add((virtualKey, down));

    public void SetModifier(KeyModifiers modifiers, bool down) => Modifiers.Add((modifiers, down));
}
