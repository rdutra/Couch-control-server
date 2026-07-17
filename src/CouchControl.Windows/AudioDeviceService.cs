using System.Runtime.InteropServices;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CouchControl.Windows;

public sealed class AudioDeviceService(ILogger<AudioDeviceService>? logger = null) : IAudioDeviceService
{
    private const string AudioRenderRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    private const string DeviceFriendlyNameProperty = "{a45c254e-df1c-4efd-8020-67d146a850e0},14";
    private const string DeviceDescriptionProperty = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";

    public Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync(CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out defaultDevice));
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            var devices = new List<AudioDeviceInfo>((int)count);
            string? defaultId = defaultDevice is null ? null : GetDeviceId(defaultDevice);

            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.GetItem(index, out device));
                    if (device is null)
                    {
                        continue;
                    }

                    var id = GetDeviceId(device);
                    var name = GetFriendlyName(device);
                    devices.Add(new AudioDeviceInfo(
                        id,
                        name,
                        string.Equals(id, defaultId, StringComparison.Ordinal)));
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(devices
                .OrderByDescending(static device => device.IsDefault)
                .ThenBy(static device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Core Audio COM enumeration failed. Falling back to registry-based audio device enumeration.");

            var fallbackDevices = GetPlaybackDevicesFromRegistry(cancellationToken);
            if (fallbackDevices.Count > 0)
            {
                return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(fallbackDevices);
            }

            throw;
        }
        finally
        {
            ReleaseComObject(defaultDevice);
            ReleaseComObject(collection);
            ReleaseComObject(enumerator);
        }
    }

    public Task<OperationResult> SetDefaultPlaybackDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Task.FromResult(OperationResult.Failure(
                "An audio device ID must be provided.",
                "audio_device_id_missing",
                outcome: "Failure"));
        }

        IPolicyConfig? policyConfig = null;
        try
        {
            policyConfig = (IPolicyConfig)new PolicyConfigClientComObject();
            Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole));
            Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia));
            Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications));

            var defaultId = TryGetDefaultPlaybackDeviceId();
            if (!string.Equals(defaultId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(OperationResult.Failure(
                    "Windows did not apply the selected playback device as the default endpoint.",
                    "audio_device_switch_not_applied",
                    outcome: "Failure"));
            }

            return Task.FromResult(OperationResult.Success("Default playback device updated."));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to set default playback device.");
            return Task.FromResult(OperationResult.Failure(
                $"Failed to set default playback device: {ex.Message}",
                "audio_device_switch_failed",
                outcome: "Failure"));
        }
        finally
        {
            ReleaseComObject(policyConfig);
        }
    }

    private static string GetDeviceId(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out var idPointer));
        try
        {
            return Marshal.PtrToStringUni(idPointer)
                ?? throw new InvalidOperationException("Audio device ID could not be read.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(idPointer);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(StorageAccessMode.Read, out var propertyStore));
        try
        {
            var key = PropertyKeys.DeviceFriendlyName;
            Marshal.ThrowExceptionForHR(propertyStore.GetValue(ref key, out var value));
            try
            {
                return value.VarType == (ushort)VarEnum.VT_LPWSTR && value.PointerValue != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.PointerValue) ?? "Unknown audio device"
                    : "Unknown audio device";
            }
            finally
            {
                value.Dispose();
            }
        }
        finally
        {
            ReleaseComObject(propertyStore);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Audio device management is supported only on Windows.");
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    private IReadOnlyList<AudioDeviceInfo> GetPlaybackDevicesFromRegistry(CancellationToken cancellationToken)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var renderKey = baseKey.OpenSubKey(AudioRenderRegistryPath);
        if (renderKey is null)
        {
            return Array.Empty<AudioDeviceInfo>();
        }

        var defaultId = TryGetDefaultPlaybackDeviceId();
        var devices = new List<AudioDeviceInfo>();

        foreach (var subKeyName in renderKey.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var deviceKey = renderKey.OpenSubKey(subKeyName);
            if (deviceKey is null || !IsActiveDevice(deviceKey))
            {
                continue;
            }

            using var propertiesKey = deviceKey.OpenSubKey("Properties");
            var friendlyName = BuildFriendlyName(
                GetStringValue(propertiesKey, DeviceFriendlyNameProperty),
                GetStringValue(propertiesKey, DeviceDescriptionProperty),
                subKeyName);

            devices.Add(new AudioDeviceInfo(
                subKeyName,
                friendlyName,
                string.Equals(subKeyName, defaultId, StringComparison.OrdinalIgnoreCase)));
        }

        return devices
            .OrderByDescending(static device => device.IsDefault)
            .ThenBy(static device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string? TryGetDefaultPlaybackDeviceId()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? defaultDevice = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out defaultDevice);
            if (hr < 0 || defaultDevice is null)
            {
                return null;
            }

            return GetDeviceId(defaultDevice);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to resolve the default playback device while using registry fallback.");
            return null;
        }
        finally
        {
            ReleaseComObject(defaultDevice);
            ReleaseComObject(enumerator);
        }
    }

    private static bool IsActiveDevice(RegistryKey deviceKey)
    {
        var deviceState = deviceKey.GetValue("DeviceState");
        return deviceState is null || Convert.ToUInt32(deviceState) == (uint)DeviceState.Active;
    }

    private static string? GetStringValue(RegistryKey? key, string valueName)
    {
        var value = key?.GetValue(valueName);
        return value as string;
    }

    private static string BuildFriendlyName(string? endpointName, string? adapterName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return string.IsNullOrWhiteSpace(adapterName) ? fallback : adapterName;
        }

        if (string.IsNullOrWhiteSpace(adapterName) ||
            endpointName.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
        {
            return endpointName;
        }

        return $"{endpointName} ({adapterName})";
    }

    private static class StorageAccessMode
    {
        public const int Read = 0;
    }

    [Flags]
    private enum DeviceState : uint
    {
        Active = 0x00000001
    }

    private enum EDataFlow
    {
        eRender = 0
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant : IDisposable
    {
        [FieldOffset(0)]
        public ushort VarType;

        [FieldOffset(8)]
        public IntPtr PointerValue;

        public void Dispose() => PropVariantClear(ref this);
    }

    private static class PropertyKeys
    {
        public static readonly PropertyKey DeviceFriendlyName =
            new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    [ComImport]
    [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClientComObject;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice defaultDevice);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0A8A2F1DBFA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetItem(uint deviceNumber, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(int storageAccessMode, out IPropertyStore properties);

        [PreserveSig]
        int GetId(out IntPtr id);

        [PreserveSig]
        int GetState(out DeviceState state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mixFormat);

        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, out IntPtr deviceFormat);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

        [PreserveSig]
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, out long defaultDevicePeriod, out long minimumDevicePeriod);

        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long devicePeriod);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

        [PreserveSig]
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isFxStore, ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isFxStore, ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
