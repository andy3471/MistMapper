namespace SteamControllerBridge.Shared;

/// <summary>Device-agnostic sample from an <c>IControllerDriver</c>.</summary>
public sealed class InputFrame
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, bool> Digitals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, float> Analogs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, (float X, float Y)> Vectors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool GetDigital(string id) =>
        Digitals.TryGetValue(id, out var v) && v;

    public float GetAnalog(string id) =>
        Analogs.TryGetValue(id, out var v) ? v : 0f;

    public bool TryGetVector(string id, out float x, out float y)
    {
        if (Vectors.TryGetValue(id, out var v))
        {
            x = v.X;
            y = v.Y;
            return true;
        }
        x = y = 0;
        return false;
    }

    public IReadOnlyList<string> PressedDigitalIds() =>
        Digitals.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
}
