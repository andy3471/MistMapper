using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MistMapper.Host.Services;

/// <summary>Extracts a Windows shell icon for a game exe and saves it as PNG.</summary>
public static class GameIconExtractor
{
    const uint ShgfiIcon = 0x000000100;
    const uint ShgfiLargeIcon = 0x000000000;

    public static bool TryWritePng(string exePath, string destPngPath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return false;

        nint hIcon = nint.Zero;
        try
        {
            var info = new ShFileInfo();
            var result = SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon);
            if (result == nint.Zero || info.hIcon == nint.Zero)
            {
                using var fallback = Icon.ExtractAssociatedIcon(exePath);
                if (fallback is null) return false;
                using var bmp = fallback.ToBitmap();
                bmp.Save(destPngPath, ImageFormat.Png);
                return true;
            }

            hIcon = info.hIcon;
            using var icon = Icon.FromHandle(hIcon);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(destPngPath, ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hIcon != nint.Zero)
                DestroyIcon(hIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileInfo
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(nint hIcon);
}
