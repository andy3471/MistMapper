using System.Runtime.InteropServices;
using System.Text;

namespace MistMapper.Host.Steam;

/// <summary>Resolves Windows device Container IDs so composite USB HID interfaces collapse to one pad.</summary>
public static class DeviceContainerId
{
    // DEVPKEY_Device_ContainerId
    // {8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C}, 2
    static readonly DevPropKey ContainerIdKey = new(
        new Guid(0x8c7ed206, 0x3f8a, 0x4827, 0xb3, 0xab, 0xae, 0x9e, 0x1f, 0xaa, 0xfc, 0x6c),
        2);

    const uint DevPropTypeGuid = 0x0000000D; // DEVPROP_TYPE_GUID
    const int CrSuccess = 0;

    public static bool TryGet(string devicePath, out Guid containerId)
    {
        containerId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(devicePath))
            return false;

        try
        {
            var instanceId = ToDeviceInstanceId(devicePath);
            if (string.IsNullOrEmpty(instanceId))
                return false;

            if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess)
                return false;

            uint type = 0;
            uint size = 16;
            var buffer = new byte[16];
            var key = ContainerIdKey;
            if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0) != CrSuccess)
                return false;
            if (type != DevPropTypeGuid || size < 16)
                return false;

            containerId = new Guid(buffer);
            return containerId != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// \\?\hid#vid_..&amp;pid_..&amp;mi_02&amp;col03#8&amp;abc&amp;0&amp;0002#{guid}
    /// → HID\VID_..\PID_..\MI_02\COL03\8&amp;ABC&amp;0&amp;0002
    /// </summary>
    public static string ToDeviceInstanceId(string devicePath)
    {
        var path = devicePath.Trim();
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path[4..];
        if (path.StartsWith(@"\\.\\", StringComparison.Ordinal))
            path = path[4..];

        var brace = path.LastIndexOf('{');
        if (brace > 0)
            path = path[..brace].TrimEnd('\\', '#');

        // Drop trailing class aliases like \kbd
        var slashAlias = path.LastIndexOf('\\');
        if (slashAlias > 0 && path.IndexOf('#', slashAlias) < 0)
        {
            var tail = path[(slashAlias + 1)..];
            if (tail.Equals("kbd", StringComparison.OrdinalIgnoreCase)
                || tail.Equals("mou", StringComparison.OrdinalIgnoreCase))
                path = path[..slashAlias];
        }

        path = path.Replace('#', '\\');
        return path.ToUpperInvariant();
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

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
