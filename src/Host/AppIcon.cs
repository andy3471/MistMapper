namespace MistMapper.Host;

static class AppIcon
{
    public static Icon Load()
    {
        var asm = typeof(AppIcon).Assembly;
        using var stream = asm.GetManifestResourceStream("MistMapper.Host.Assets.MistMapper.ico")
            ?? throw new InvalidOperationException("Embedded MistMapper.ico missing.");
        // Icon(Stream) requires the stream to stay open for some usages; clone via handle.
        using var temp = new Icon(stream);
        return (Icon)temp.Clone();
    }
}
