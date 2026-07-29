using System.Runtime.InteropServices;
using System.Text;
using MistMapper.Host.Steam;

namespace MistMapper.Host.DualSense;

/// <summary>
/// Temporarily disables the DualSense's standard HID gamepad interface (MI_03)
/// so games only see MistMapper's virtual Xbox pad. Opening HID streams does not
/// hide the device on Windows — reports are fanned out to every consumer.
/// Requires elevation (CM_Disable_DevNode).
/// </summary>
public sealed class NativeGamepadHider : IDisposable
{
    const int CrSuccess = 0;
    const uint CmGetIdListFilterPresent = 0x00000100;

    readonly List<string> _hiddenInstanceIds = [];
    bool _disposed;

    public IReadOnlyList<string> HiddenInstanceIds => _hiddenInstanceIds;
    public bool HasHidden => _hiddenInstanceIds.Count > 0;

    /// <summary>
    /// Disable native gamepad interfaces that share the same device container as
    /// <paramref name="primaryHidPath"/> (the vendor interface MistMapper reads).
    /// </summary>
    public bool TryHideForDevice(string primaryHidPath)
    {
        Restore();
        if (string.IsNullOrWhiteSpace(primaryHidPath))
            return false;
        if (!DeviceContainerId.TryGet(primaryHidPath, out var container))
            return false;

        foreach (var instanceId in EnumeratePresentInstanceIds())
        {
            if (!IsDualSenseNativeGamepad(instanceId))
                continue;
            if (!TryGetContainer(instanceId, out var other) || other != container)
                continue;
            if (!TryDisable(instanceId))
                continue;
            _hiddenInstanceIds.Add(instanceId);
        }

        return _hiddenInstanceIds.Count > 0;
    }

    public void Restore()
    {
        foreach (var id in _hiddenInstanceIds)
            TryEnable(id);
        _hiddenInstanceIds.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Restore();
    }

    static bool IsDualSenseNativeGamepad(string instanceId)
    {
        // DualSense / Edge expose a standard gamepad on MI_03.
        if (instanceId.Contains("VID_054C&PID_0CE6&MI_03", StringComparison.OrdinalIgnoreCase)
            || instanceId.Contains("VID_054C&PID_0DF2&MI_03", StringComparison.OrdinalIgnoreCase))
        {
            // Prefer the HID game controller node; skipping the intermediate USB Input Device
            // is fine — disabling the leaf HID node removes it from DirectInput/Raw Input.
            return instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    static bool TryGetContainer(string instanceId, out Guid containerId)
    {
        containerId = Guid.Empty;
        try
        {
            if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess)
                return false;

            // DEVPKEY_Device_ContainerId
            var key = new DevPropKey(
                new Guid(0x8c7ed206, 0x3f8a, 0x4827, 0xb3, 0xab, 0xae, 0x9e, 0x1f, 0xaa, 0xfc, 0x6c),
                2);
            uint type = 0;
            uint size = 16;
            var buffer = new byte[16];
            if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0) != CrSuccess)
                return false;
            if (type != 0x0000000D || size < 16)
                return false;
            containerId = new Guid(buffer);
            return containerId != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }

    static bool TryDisable(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess)
                return false;
            return CM_Disable_DevNode(devInst, 0) == CrSuccess;
        }
        catch
        {
            return false;
        }
    }

    static bool TryEnable(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess)
                return false;
            return CM_Enable_DevNode(devInst, 0) == CrSuccess;
        }
        catch
        {
            return false;
        }
    }

    static IEnumerable<string> EnumeratePresentInstanceIds()
    {
        if (CM_Get_Device_ID_List_SizeW(out uint chars, null, CmGetIdListFilterPresent) != CrSuccess
            || chars == 0)
            yield break;

        var buf = new char[chars];
        if (CM_Get_Device_ID_ListW(null, buf, chars, CmGetIdListFilterPresent) != CrSuccess)
            yield break;

        var sb = new StringBuilder();
        for (int i = 0; i < buf.Length; i++)
        {
            if (buf[i] == '\0')
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                // Double-null terminates the list.
                if (i + 1 < buf.Length && buf[i + 1] == '\0')
                    yield break;
            }
            else
            {
                sb.Append(buf[i]);
            }
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    static extern int CM_Disable_DevNode(uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    static extern int CM_Enable_DevNode(uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string? pszFilter, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_ID_ListW(string? pszFilter, [Out] char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DevPropKey propertyKey,
        out uint propertyType,
        byte[]? propertyBuffer,
        ref uint propertyBufferSize,
        uint ulFlags);

    [StructLayout(LayoutKind.Sequential)]
    struct DevPropKey
    {
        public Guid Fmtid;
        public uint Pid;

        public DevPropKey(Guid fmtid, uint pid)
        {
            Fmtid = fmtid;
            Pid = pid;
        }
    }
}
