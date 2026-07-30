using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace MistMapper.Installer;

sealed class InstallOptions
{
    public bool InstallHost { get; set; } = true;
    public bool InstallGameBarWidget { get; set; } = true;
    public bool InstallViiper { get; set; } = true;
    public bool InstallUsbip { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool LaunchWhenDone { get; set; } = true;
}

sealed class InstallEngine
{
    public static string InstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "MistMapper");

    public static string HostExePath => Path.Combine(InstallRoot, "Host", "MistMapper.exe");

    const string ViiperVersion = "v0.7.0";
    const string UsbipVersionTag = "v.0.9.7.8";
    const string UsbipAsset = "USBip-0.9.7.8-x64.exe";

    readonly Action<string> _log;
    readonly Action<int> _progress;

    public InstallEngine(Action<string> log, Action<int> progress)
    {
        _log = log;
        _progress = progress;
    }

    public async Task RunAsync(InstallOptions options, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "MistMapper-Setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            _progress(2);
            _log("Preparing payload…");
            var payloadRoot = await PreparePayloadAsync(work, ct);

            if (options.InstallHost)
            {
                _progress(15);
                InstallHostFiles(payloadRoot);
            }

            if (options.InstallGameBarWidget)
            {
                _progress(30);
                await InstallGameBarWidgetAsync(payloadRoot, ct);
            }

            if (options.InstallViiper)
            {
                _progress(55);
                await InstallViiperAsync(ct);
            }

            if (options.InstallUsbip)
            {
                _progress(70);
                await InstallUsbipAsync(ct);
            }

            if (options.StartWithWindows)
            {
                _progress(85);
                EnableStartup();
            }

            _progress(90);
            CreateShortcuts();

            if (options.LaunchWhenDone)
            {
                _progress(95);
                LaunchApps(options.InstallViiper);
            }

            _progress(100);
            _log("Done.");
            _log("");
            _log("Installed to: " + InstallRoot);
            _log("Press Win+G → Widgets → pin MistMapper.");
            if (options.StartWithWindows)
                _log("Auto-launch registered (Start with Windows).");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
        }
    }

    async Task<string> PreparePayloadAsync(string work, CancellationToken ct)
    {
        // Prefer adjacent payload.zip (dev / folder layout), then embedded resource.
        var adjacent = Path.Combine(AppContext.BaseDirectory, "payload.zip");
        var extractTo = Path.Combine(work, "payload");
        Directory.CreateDirectory(extractTo);

        if (File.Exists(adjacent))
        {
            _log("Extracting payload.zip…");
            await Task.Run(() => ZipFile.ExtractToDirectory(adjacent, extractTo, overwriteFiles: true), ct);
            return extractTo;
        }

        await using var embedded = typeof(InstallEngine).Assembly
            .GetManifestResourceStream("MistMapper.Installer.payload.zip");
        if (embedded is null)
            throw new InvalidOperationException(
                "No payload.zip found. Rebuild with scripts\\build-installer.ps1.");

        var zipPath = Path.Combine(work, "payload.zip");
        await using (var fs = File.Create(zipPath))
            await embedded.CopyToAsync(fs, ct);

        _log("Extracting embedded payload…");
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractTo, overwriteFiles: true), ct);
        return extractTo;
    }

    void InstallHostFiles(string payloadRoot)
    {
        var src = Path.Combine(payloadRoot, "Host");
        if (!Directory.Exists(src))
            throw new InvalidOperationException("Payload missing Host\\ folder.");

        var dest = Path.Combine(InstallRoot, "Host");
        _log("Installing host → " + dest);
        if (Directory.Exists(dest))
            Directory.Delete(dest, recursive: true);
        CopyDirectory(src, dest);

        if (!File.Exists(HostExePath))
            throw new InvalidOperationException("MistMapper.exe missing after copy.");
    }

    async Task InstallGameBarWidgetAsync(string payloadRoot, CancellationToken ct)
    {
        var widgetDir = Path.Combine(payloadRoot, "GameBarWidget");
        if (!Directory.Exists(widgetDir))
            throw new InvalidOperationException("Payload missing GameBarWidget\\ folder.");

        var msix = Directory.GetFiles(widgetDir, "*.msix").OrderByDescending(f => f).FirstOrDefault()
            ?? throw new InvalidOperationException("No .msix in GameBarWidget payload.");
        var cer = Directory.GetFiles(widgetDir, "*.cer").OrderByDescending(f => f).FirstOrDefault();

        _log("Enabling sideloading…");
        EnableSideloading();

        if (cer is not null)
        {
            _log("Trusting widget certificate…");
            TrustCertificate(cer);
        }

        _log("Removing previous Game Bar widget (if any)…");
        await RemoveOldWidgetPackagesAsync(ct);

        var depsDir = Path.Combine(widgetDir, "Dependencies", "x64");
        var deps = Directory.Exists(depsDir)
            ? Directory.GetFiles(depsDir, "*.appx").Select(p => new Uri(p)).ToList()
            : [];

        _log("Installing " + Path.GetFileName(msix) + "…");
        var pm = new PackageManager();
        var result = await pm.AddPackageAsync(
            new Uri(msix),
            deps,
            DeploymentOptions.ForceApplicationShutdown).AsTask(ct);

        if (!result.IsRegistered)
        {
            var err = result.ExtendedErrorCode.ToString();
            throw new InvalidOperationException("AddPackage failed: " + err);
        }

        _log("Game Bar widget registered.");

        foreach (var name in new[] { "GameBar", "GameBarFTServer", "XboxGameBarWidgets" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }
    }

    static void EnableSideloading()
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
        key.SetValue("AllowDevelopmentWithoutDevLicense", 1, RegistryValueKind.DWord);
        key.SetValue("AllowAllTrustedApps", 1, RegistryValueKind.DWord);
    }

    static void TrustCertificate(string cerPath)
    {
        using var cert = new X509Certificate2(cerPath);
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var exists = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false);
        if (exists.Count == 0)
            store.Add(cert);
    }

    static async Task RemoveOldWidgetPackagesAsync(CancellationToken ct)
    {
        var pm = new PackageManager();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MistMapper.GameBar",
            "SteamControllerBridge.GameBar"
        };
        foreach (var pkg in pm.FindPackagesForUser(string.Empty).ToList())
        {
            if (!names.Contains(pkg.Id.Name)) continue;
            try
            {
                await pm.RemovePackageAsync(pkg.Id.FullName).AsTask(ct);
            }
            catch
            {
                // Best-effort; AddPackage can still upgrade.
            }
        }
    }

    async Task InstallViiperAsync(CancellationToken ct)
    {
        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIIPER");
        var exe = Path.Combine(dest, "viiper.exe");
        Directory.CreateDirectory(dest);

        if (!File.Exists(exe))
        {
            var url = $"https://github.com/Alia5/VIIPER/releases/download/{ViiperVersion}/viiper-windows-amd64.zip";
            _log("Downloading VIIPER (" + ViiperVersion + ")…");
            var zip = Path.Combine(Path.GetTempPath(), "viiper-windows-amd64.zip");
            await DownloadAsync(url, zip, ct);
            _log("Extracting VIIPER…");
            ZipFile.ExtractToDirectory(zip, dest, overwriteFiles: true);
            try { File.Delete(zip); } catch { /* ignore */ }
        }
        else
        {
            _log("VIIPER already present.");
        }

        if (!File.Exists(exe))
            throw new InvalidOperationException("viiper.exe not found after install.");

        _log("VIIPER ready at " + exe + " (GPL-3.0 — https://github.com/Alia5/VIIPER)");
        AddUserPath(dest);
    }

    async Task InstallUsbipAsync(CancellationToken ct)
    {
        var usbipOk = File.Exists(@"C:\Program Files\USBip\usbip.exe")
                      || File.Exists(@"C:\Program Files (x86)\USBip\usbip.exe");
        if (usbipOk)
        {
            _log("usbip-win2 already installed.");
            return;
        }

        var destDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "usbip-win2");
        Directory.CreateDirectory(destDir);
        var installer = Path.Combine(destDir, UsbipAsset);
        if (!File.Exists(installer))
        {
            var url = $"https://github.com/vadimgrn/usbip-win2/releases/download/{UsbipVersionTag}/{UsbipAsset}";
            _log("Downloading usbip-win2…");
            await DownloadAsync(url, installer, ct);
        }

        _log("Launching usbip-win2 installer (driver needs UAC confirmation)…");
        var psi = new ProcessStartInfo
        {
            FileName = installer,
            UseShellExecute = true,
            Verb = "runas"
        };
        using var proc = Process.Start(psi);
        if (proc is not null)
        {
            await proc.WaitForExitAsync(ct);
            _log(proc.ExitCode == 0
                ? "usbip-win2 installer finished."
                : "usbip-win2 installer exited with code " + proc.ExitCode + " (you can re-run it later).");
        }
    }

    void EnableStartup()
    {
        _log("Registering Start with Windows…");
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run");
        key.DeleteValue("SteamControllerBridge", throwOnMissingValue: false);
        key.SetValue("MistMapper", $"\"{HostExePath}\" --tray");

        // Keep profile flag in sync when host first runs; also write AppData hint.
        try
        {
            var profiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MistMapper", "profiles.json");
            // Host creates this; we only ensure Run key here.
            _ = profiles;
        }
        catch { /* ignore */ }
    }

    void CreateShortcuts()
    {
        try
        {
            var programs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "MistMapper");
            Directory.CreateDirectory(programs);
            CreateShortcut(Path.Combine(programs, "MistMapper.lnk"), HostExePath, "--tray", InstallRoot);
            CreateShortcut(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "MistMapper.lnk"),
                HostExePath, "--tray", InstallRoot);
            _log("Start Menu + Desktop shortcuts created.");
        }
        catch (Exception ex)
        {
            _log("Shortcut warning: " + ex.Message);
        }
    }

    static void CreateShortcut(string lnkPath, string target, string args, string workDir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        var shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = target;
        shortcut.Arguments = args;
        shortcut.WorkingDirectory = workDir;
        shortcut.Description = "MistMapper";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    void LaunchApps(bool startViiper)
    {
        if (startViiper)
        {
            var viiper = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VIIPER", "viiper.exe");
            if (File.Exists(viiper) && !IsPortOpen(3242))
            {
                _log("Starting VIIPER…");
                var psi = new ProcessStartInfo
                {
                    FileName = viiper,
                    Arguments = "server --api.auto-attach-local-client=false",
                    WorkingDirectory = Path.GetDirectoryName(viiper),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var usbipDirs = new[]
                {
                    @"C:\Program Files\USBip",
                    @"C:\Program Files (x86)\USBip"
                };
                var prefix = string.Join(";", usbipDirs.Where(d => File.Exists(Path.Combine(d, "usbip.exe"))));
                if (!string.IsNullOrEmpty(prefix))
                    psi.Environment["Path"] = prefix + ";" + Environment.GetEnvironmentVariable("Path");
                Process.Start(psi);
            }
        }

        if (File.Exists(HostExePath))
        {
            _log("Starting MistMapper…");
            Process.Start(new ProcessStartInfo
            {
                FileName = HostExePath,
                Arguments = "--tray",
                WorkingDirectory = Path.GetDirectoryName(HostExePath),
                UseShellExecute = true
            });
        }
    }

    static bool IsPortOpen(int port)
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            c.Connect("127.0.0.1", port);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void AddUserPath(string dir)
    {
        var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        if (userPath.Contains(dir, StringComparison.OrdinalIgnoreCase)) return;
        var next = string.IsNullOrWhiteSpace(userPath) ? dir : userPath.TrimEnd(';') + ";" + dir;
        Environment.SetEnvironmentVariable("Path", next, EnvironmentVariableTarget.User);
    }

    static async Task DownloadAsync(string url, string dest, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await using var remote = await http.GetStreamAsync(url, ct);
        await using var local = File.Create(dest);
        await remote.CopyToAsync(local, ct);
    }

    static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
