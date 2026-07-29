using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

public interface IMouseSink
{
    void Move(int dx, int dy);
    void SetButton(MouseButtonOutput button, bool down);
}
