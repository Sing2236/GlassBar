using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace GlassBar.Services;

public sealed class SystemMetricsService
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;

    public int CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);
        var kernelDelta = kernelValue - _previousKernel;
        var userDelta = userValue - _previousUser;
        var idleDelta = idleValue - _previousIdle;
        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;
        var total = kernelDelta + userDelta;
        return total == 0 ? 0 : (int)Math.Clamp(Math.Round(100d * (total - idleDelta) / total), 0, 100);
    }

    public int MemoryPercent()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (int)status.MemoryLoad : 0;
    }

    public string ConnectionLabel()
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) return "Offline";
        var active = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(adapter =>
            adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel);
        return active?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Online";
    }

    public string PowerLabel()
    {
        if (!GetSystemPowerStatus(out var status)) return "Power";
        if (status.BatteryFlag == 128) return "Desktop";
        var percent = status.BatteryLifePercent == 255 ? "--" : $"{status.BatteryLifePercent}%";
        return status.ACLineStatus == 1 ? $"{percent} · charging" : percent;
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
