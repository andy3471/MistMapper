using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

public interface IKeyboardSink
{
    void SetKey(int virtualKey, bool down);
    void SetModifier(KeyModifiers modifiers, bool down);
}
