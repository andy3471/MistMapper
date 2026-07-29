using System.Runtime.InteropServices;
using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

public sealed class Win32KeyboardSink : IKeyboardSink
{
    public static Win32KeyboardSink Instance { get; } = new();

    const uint InputKeyboard = 1;
    const uint KeyEventExtended = 0x0001;
    const uint KeyEventKeyUp = 0x0002;
    const uint KeyEventScancode = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public KeybdInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    Win32KeyboardSink() { }

    public void SetKey(int virtualKey, bool down)
    {
        if (virtualKey <= 0) return;
        var scan = (ushort)MapVirtualKey((uint)virtualKey, 0);
        var flags = KeyEventScancode | (down ? 0u : KeyEventKeyUp);
        if (IsExtended(virtualKey)) flags |= KeyEventExtended;
        var input = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Ki = new KeybdInput { Vk = 0, Scan = scan, Flags = flags }
            }
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            _ = Marshal.GetLastWin32Error();
    }

    public void SetModifier(KeyModifiers modifiers, bool down)
    {
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) SetKey(0x11, down);
        if (modifiers.HasFlag(KeyModifiers.Alt)) SetKey(0x12, down);
        if (modifiers.HasFlag(KeyModifiers.Shift)) SetKey(0x10, down);
        if (modifiers.HasFlag(KeyModifiers.Win)) SetKey(0x5B, down);
    }

    static bool IsExtended(int vk) => vk is 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x21 or 0x22 or 0x23 or 0x24 or 0x5B or 0x5C;
}
