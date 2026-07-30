namespace MistMapper.Shared;

/// <summary>Steam-style activator for a binding on one physical input.</summary>
public enum ActivatorType
{
    /// <summary>Held for the duration of the press (default).</summary>
    Regular,
    /// <summary>Fires after a hold threshold; interrupts Regular bindings on that input.</summary>
    LongPress
}

/// <summary>One activator entry: up to two simultaneous output actions.</summary>
public sealed class InputBinding
{
    public ActivatorType Activator { get; set; } = ActivatorType.Regular;

    /// <summary>1–2 outputs fired together for this activator.</summary>
    public List<OutputAction> Actions { get; set; } = [];

    public static InputBinding FromAction(OutputAction action, ActivatorType activator = ActivatorType.Regular) =>
        new()
        {
            Activator = activator,
            Actions = action.Kind == OutputActionKind.None ? [] : [CloneAction(action)]
        };

    public static InputBinding Clone(InputBinding? b)
    {
        if (b is null) return new InputBinding();
        return new InputBinding
        {
            Activator = b.Activator,
            Actions = b.Actions.Select(CloneAction).Where(a => a.Kind != OutputActionKind.None).ToList()
        };
    }

    public string ToDisplayString()
    {
        var parts = Actions
            .Where(a => a.Kind != OutputActionKind.None)
            .Select(a => a.ToDisplayString())
            .ToList();
        if (parts.Count == 0) return "None";
        var body = string.Join(" + ", parts);
        return Activator == ActivatorType.LongPress ? $"Long: {body}" : body;
    }

    static OutputAction CloneAction(OutputAction a) => new()
    {
        Kind = a.Kind,
        Xbox = a.Xbox,
        VirtualKey = a.VirtualKey,
        Modifiers = a.Modifiers,
        MouseButton = a.MouseButton
    };
}
