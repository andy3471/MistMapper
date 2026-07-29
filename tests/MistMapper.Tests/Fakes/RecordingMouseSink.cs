using MistMapper.Host.Mapping;
using MistMapper.Shared;

namespace MistMapper.Tests.Fakes;

public sealed class RecordingMouseSink : IMouseSink
{
    public List<(int Dx, int Dy)> Moves { get; } = [];
    public List<(MouseButtonOutput Button, bool Down)> Buttons { get; } = [];

    public void Move(int dx, int dy) => Moves.Add((dx, dy));

    public void SetButton(MouseButtonOutput button, bool down) => Buttons.Add((button, down));
}
