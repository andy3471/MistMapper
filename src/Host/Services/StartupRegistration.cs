using Microsoft.Win32;

namespace SteamControllerBridge.Host.Services;

/// <summary>
/// Registers the host for logon startup. Documents FSE "Start at log in" requirement.
/// </summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "SteamControllerBridge";

    public static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SteamControllerBridge.exe");

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{ExePath}\" --tray");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Writes a helper script that users can run elevated to nudge FSE startup category.
    /// Windows FSE "Start at log in" is primarily controlled via Settings &gt; Apps &gt; Startup;
    /// we also set HKCU Run as Path 1 baseline.
    /// </summary>
    public static void WriteFseHelperScript(string directory)
    {
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "enable-fse-startup.ps1");
        File.WriteAllText(script, """
            # Steam Controller Bridge — FSE / Xbox mode startup helper (Path 1)
            # 1) Ensures HKCU Run registration (Start at logon)
            # 2) Prints instructions for Settings > Apps > Startup > "Start at log in"
            #    so the host runs inside Xbox Full Screen Experience.

            $exe = Join-Path $PSScriptRoot '..\SteamControllerBridge.exe'
            if (-not (Test-Path $exe)) {
              $exe = (Get-Command SteamControllerBridge -ErrorAction SilentlyContinue)?.Source
            }
            if (-not $exe -or -not (Test-Path $exe)) {
              Write-Error 'SteamControllerBridge.exe not found. Build/publish the Host first.'
              exit 1
            }

            $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
            New-Item -Path $run -Force | Out-Null
            Set-ItemProperty -Path $run -Name 'SteamControllerBridge' -Value ('"{0}" --tray' -f (Resolve-Path $exe))
            Write-Host "Registered Run key for: $exe"

            Write-Host ""
            Write-Host "Xbox mode / FSE (Path 1):"
            Write-Host "  Settings > Apps > Startup > Steam Controller Bridge > Start at log in"
            Write-Host "Do NOT choose 'Start when exiting to desktop'."
            Write-Host ""
            Write-Host "AnyFSE (Path 2): set custom startup application to this exe."
            Write-Host "Path 3 (optional FseHome MSIX): see docs/setup.md"
            """);
    }
}
