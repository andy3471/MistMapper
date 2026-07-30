using System.Runtime.InteropServices;
using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

public sealed class Win32MouseSink : IMouseSink
{
    public static Win32MouseSink Instance { get; } = new();

    const uint InputMouse = 0;
    const uint MouseMove = 0x0001;
    const uint MouseLeftDown = 0x0002;
    const uint MouseLeftUp = 0x0004;
    const uint MouseRightDown = 0x0008;
    const uint MouseRightUp = 0x0010;
    const uint MouseMiddleDown = 0x0020;
    const uint MouseMiddleUp = 0x0040;
    const uint MouseWheel = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    struct Input
    {
        public uint Type;
        public MouseInput Mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    Win32MouseSink() { }

    public void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var input = new Input
        {
            Type = InputMouse,
            Mi = new MouseInput { Dx = dx, Dy = dy, Flags = MouseMove }
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            _ = Marshal.GetLastWin32Error();
    }

    public void SetButton(MouseButtonOutput button, bool down)
    {
        uint flags = button switch
        {
            MouseButtonOutput.Left => down ? MouseLeftDown : MouseLeftUp,
            MouseButtonOutput.Right => down ? MouseRightDown : MouseRightUp,
            MouseButtonOutput.Middle => down ? MouseMiddleDown : MouseMiddleUp,
            _ => 0
        };
        if (flags == 0) return;
        var input = new Input { Type = InputMouse, Mi = new MouseInput { Flags = flags } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            _ = Marshal.GetLastWin32Error();
    }

    public void Scroll(int wheelDelta)
    {
        if (wheelDelta == 0) return;
        var input = new Input
        {
            Type = InputMouse,
            Mi = new MouseInput { MouseData = unchecked((uint)wheelDelta), Flags = MouseWheel }
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            _ = Marshal.GetLastWin32Error();
    }

    public void LeftClick()
    {
        SetButton(MouseButtonOutput.Left, true);
        SetButton(MouseButtonOutput.Left, false);
    }
}
