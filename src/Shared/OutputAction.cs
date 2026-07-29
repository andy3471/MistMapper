namespace MistMapper.Shared;

public enum OutputActionKind
{
    None,
    Xbox,
    Key,
    MouseButton
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

public enum MouseButtonOutput
{
    Left,
    Right,
    Middle
}

/// <summary>One remappable output: Xbox button, keyboard key, or mouse button.</summary>
public sealed class OutputAction
{
    public OutputActionKind Kind { get; set; } = OutputActionKind.None;
    public XboxOutput Xbox { get; set; } = XboxOutput.None;
    public int VirtualKey { get; set; }
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.None;
    public MouseButtonOutput MouseButton { get; set; } = MouseButtonOutput.Left;

    public static OutputAction None() => new() { Kind = OutputActionKind.None };

    public static OutputAction FromXbox(XboxOutput xbox) =>
        xbox == XboxOutput.None
            ? None()
            : new OutputAction { Kind = OutputActionKind.Xbox, Xbox = xbox };

    public static OutputAction FromKey(int virtualKey, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Kind = OutputActionKind.Key, VirtualKey = virtualKey, Modifiers = modifiers };

    public static OutputAction FromMouse(MouseButtonOutput button) =>
        new() { Kind = OutputActionKind.MouseButton, MouseButton = button };

    public string ToDisplayString()
    {
        return Kind switch
        {
            OutputActionKind.Xbox => Xbox.ToString(),
            OutputActionKind.Key => FormatKey(VirtualKey, Modifiers),
            OutputActionKind.MouseButton => "Mouse" + MouseButton,
            _ => "None"
        };
    }

    static string FormatKey(int vk, KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(VirtualKeyNames.GetName(vk));
        return string.Join("+", parts);
    }
}

public static class VirtualKeyNames
{
    public static string GetName(int vk) => vk switch
    {
        0 => "None",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
        _ => $"VK_{vk:X2}"
    };
}
