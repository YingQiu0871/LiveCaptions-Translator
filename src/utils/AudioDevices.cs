using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace LiveCaptionsTranslator.utils
{
    public record AudioDevice(string Id, string Name, bool IsDefault)
    {
        public override string ToString() => IsDefault ? $"{Name}（当前默认）" : Name;
    }

    public static class AudioDevices
    {
        public static List<AudioDevice> List(DataFlow flow)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                string? defaultId = DefaultId(enumerator, flow);
                return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
                    .Select(device => new AudioDevice(device.ID, device.FriendlyName, device.ID == defaultId))
                    .ToList();
            }
            catch (Exception)
            {
                return new List<AudioDevice>();
            }
        }

        public static string? DefaultId(DataFlow flow)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                return DefaultId(enumerator, flow);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow) =>
            enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)
                ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID
                : null;

        public static MMDevice? Get(string id)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                return enumerator.GetDevice(id);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Makes the device the Windows default for all roles (what LiveCaptions listens to).
        // Uses the undocumented but long-stable IPolicyConfig interface, as the Sound control panel does.
        public static void SetDefault(string id)
        {
            var policyConfig = (IPolicyConfig)new CPolicyConfigClient();
            try
            {
                Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(id, 0));
                Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(id, 1));
                Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(id, 2));
            }
            finally
            {
                Marshal.ReleaseComObject(policyConfig);
            }
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class CPolicyConfigClient
        {
        }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr format);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isFxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isFxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isVisible);
        }
    }
}
