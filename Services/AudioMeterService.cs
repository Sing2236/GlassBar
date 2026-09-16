using System.Runtime.InteropServices;

namespace GlassBar.Services;

public sealed class AudioMeterService : IDisposable
{
    private object? _enumeratorObject;
    private object? _deviceObject;
    private IAudioMeterInformation? _meter;

    public float ReadPeak()
    {
        try
        {
            _meter ??= CreateMeter();
            return _meter.GetPeakValue(out var peak) == 0 ? Math.Clamp(peak, 0, 1) : 0;
        }
        catch
        {
            ReleaseObjects();
            return 0;
        }
    }

    private IAudioMeterInformation CreateMeter()
    {
        var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
        _enumeratorObject = Activator.CreateInstance(type)!;
        var enumerator = (IMMDeviceEnumerator)_enumeratorObject;
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device));
        _deviceObject = device;
        var iid = typeof(IAudioMeterInformation).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, nint.Zero, out var meter));
        return (IAudioMeterInformation)meter;
    }

    public void Dispose()
    {
        ReleaseObjects();
        GC.SuppressFinalize(this);
    }

    private void ReleaseObjects()
    {
        if (_meter is not null && Marshal.IsComObject(_meter)) Marshal.FinalReleaseComObject(_meter);
        _meter = null;
        if (_deviceObject is not null && Marshal.IsComObject(_deviceObject)) Marshal.FinalReleaseComObject(_deviceObject);
        if (_enumeratorObject is not null && Marshal.IsComObject(_enumeratorObject)) Marshal.FinalReleaseComObject(_enumeratorObject);
        _deviceObject = null;
        _enumeratorObject = null;
    }

    private enum EDataFlow { Render, Capture, All }
    private enum ERole { Console, Multimedia, Communications }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(nint client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, nint activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out nint properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out int count);
        [PreserveSig] int GetChannelsPeakValues(int channelCount, [Out] float[] values);
        [PreserveSig] int QueryHardwareSupport(out int mask);
    }
}
