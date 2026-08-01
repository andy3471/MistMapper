using System.Diagnostics;
using Microsoft.Win32;

namespace MistMapper.Host.Services;

/// <summary>
/// Registers the host for logon startup, including Xbox FSE "Start at log in".
/// </summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>
    /// Xbox mode whitelist: DWORD 1 = Start at log in (run inside FSE, not only after desktop).
    /// Settings → Apps → Startup may visually revert; this key is the durable state.
    /// </summary>
    const string FseStartupRunKey =
        @"Software\Microsoft\Windows\CurrentVersion\GamingConfiguration\Startup\Run";
    const string ValueName = "MistMapper";
    const int FseStartAtLogIn = 1;

    public static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MistMapper.exe");

    public static string DesiredRunValue => $"\"{ExePath}\" --tray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>
    /// Enable or disable HKCU Run + FSE Start-at-log-in whitelist.
    /// Skips rewriting the Run command when it is already correct so StartupApproved
    /// / Settings classification is not churned unnecessarily.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        // Drop legacy product name so users don't get two startup entries.
        key.DeleteValue("SteamControllerBridge", throwOnMissingValue: false);

        if (enabled)
        {
            var current = key.GetValue(ValueName) as string;
            if (!string.Equals(current, DesiredRunValue, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, DesiredRunValue);
            SetFseStartAtLogIn(true);
        }
        else
        {
            if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            SetFseStartAtLogIn(false);
        }
    }

    /// <summary>
    /// Writes or clears the Xbox FSE startup whitelist entry for MistMapper.
    /// </summary>
    public static void SetFseStartAtLogIn(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(FseStartupRunKey);
                if (IsFseStartAtLogInValue(key.GetValue(ValueName)))
                    return;
                key.SetValue(ValueName, FseStartAtLogIn, RegistryValueKind.DWord);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(FseStartupRunKey, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Absent on builds without Xbox mode / GamingConfiguration — ignore.
        }
    }

    public static bool IsFseStartAtLogInEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(FseStartupRunKey, false);
            return IsFseStartAtLogInValue(key?.GetValue(ValueName));
        }
        catch
        {
            return false;
        }
    }

    static bool IsFseStartAtLogInValue(object? value) =>
        value switch
        {
            int i => i == FseStartAtLogIn,
            long l => l == FseStartAtLogIn,
            _ => false
        };

    public static void OpenStartupAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:startupapps",
                UseShellExecute = true
            });
        }
        catch
        {
            // Settings URI may be unavailable in some sessions.
        }
    }

    /// <summary>
    /// Writes a helper script that re-applies Run + FSE whitelist and opens Startup settings.
    /// </summary>
    public static void WriteFseHelperScript(string directory)
    {
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "enable-fse-startup.ps1");
        File.WriteAllText(script, """
            # MistMapper — Xbox mode / FSE startup helper
            # Ensures HKCU Run + GamingConfiguration "Start at log in" (DWORD 1).

            $exe = Join-Path $env:LOCALAPPDATA 'Programs\MistMapper\Host\MistMapper.exe'
            if (-not (Test-Path $exe)) {
              $exe = (Get-Command MistMapper -ErrorAction SilentlyContinue)?.Source
            }
            if (-not $exe -or -not (Test-Path $exe)) {
              Write-Error 'MistMapper.exe not found. Install MistMapper Setup first.'
              exit 1
            }

            $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
            $desired = '"{0}" --tray' -f (Resolve-Path $exe)
            New-Item -Path $run -Force | Out-Null
            $current = (Get-ItemProperty -Path $run -Name 'MistMapper' -ErrorAction SilentlyContinue).MistMapper
            if ($current -ne $desired) {
              Set-ItemProperty -Path $run -Name 'MistMapper' -Value $desired
              Write-Host "Registered Run key for: $exe"
            } else {
              Write-Host "Run key already correct (left unchanged)."
            }

            $fse = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration\Startup\Run'
            New-Item -Path $fse -Force | Out-Null
            New-ItemProperty -Path $fse -Name 'MistMapper' -Value 1 -PropertyType DWord -Force | Out-Null
            Write-Host "Set Xbox mode Start at log in (GamingConfiguration\Startup\Run\MistMapper = 1)."

            Write-Host ""
            Write-Host "Settings → Apps → Startup may visually revert; the registry value above is what matters."
            Write-Host "Opening Startup apps settings…"
            Start-Process 'ms-settings:startupapps'
            """);
    }
}
