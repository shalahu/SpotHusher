using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpotHusher;

public static class SpotifyAudioController
{
    public static void SetMute(bool isMuted)
    {
        var devicesCollectionPtr = IntPtr.Zero;
        IMMDeviceEnumerator? deviceEnumerator = null;

        try
        {
            var spotifyPids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName("Spotify")) spotifyPids.Add((uint)p.Id);

            if (spotifyPids.Count == 0) return;

            var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!;
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;

            var hr = deviceEnumerator.EnumAudioEndpoints(0, 1, out devicesCollectionPtr);
            if (hr != 0 || devicesCollectionPtr == IntPtr.Zero) return;

            var vtable = Marshal.ReadIntPtr(devicesCollectionPtr);
            var getCountPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);

            var getCount =
                (GetCountDelegate)Marshal.GetDelegateForFunctionPointer(getCountPtr, typeof(GetCountDelegate));
            hr = getCount(devicesCollectionPtr, out var deviceCount);
            if (hr != 0 || deviceCount == 0) return;

            var itemPtr = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
            var getItem = (ItemDelegate)Marshal.GetDelegateForFunctionPointer(itemPtr, typeof(ItemDelegate));

            for (uint d = 0; d < deviceCount; d++)
            {
                var devicePtr = IntPtr.Zero;
                IMMDevice? device = null;
                object? sessionManagerObj = null;
                IAudioSessionEnumerator? sessionEnumerator = null;

                try
                {
                    hr = getItem(devicesCollectionPtr, d, out devicePtr);
                    if (hr != 0 || devicePtr == IntPtr.Zero) continue;

                    device = (IMMDevice)Marshal.GetObjectForIUnknown(devicePtr);

                    var iidAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                    hr = device.Activate(ref iidAudioSessionManager2, 0x17, IntPtr.Zero, out sessionManagerObj);
                    if (hr != 0 || sessionManagerObj == null)
                    {
                        continue;
                    }

                    var sessionManager = (IAudioSessionManager2)sessionManagerObj;
                    sessionManager.GetSessionEnumerator(out sessionEnumerator);
                    if (sessionEnumerator == null)
                    {
                        continue;
                    }

                    sessionEnumerator.GetCount(out var sessionCount);

                    for (var i = 0; i < sessionCount; i++)
                    {
                        var sessionControlPtr = IntPtr.Zero;
                        try
                        {
                            hr = sessionEnumerator.GetSession(i, out sessionControlPtr);
                            if (hr != 0 || sessionControlPtr == IntPtr.Zero) continue;

                            var sessionControl2 =
                                (IAudioSessionControl2)Marshal.GetObjectForIUnknown(sessionControlPtr);
                            sessionControl2.GetProcessId(out var audioPid);

                            var isSpotifyAudio = false;

                            if (audioPid != 0)
                            {
                                if (spotifyPids.Contains(audioPid))
                                {
                                    isSpotifyAudio = true;
                                }
                                else
                                {
                                    var parentPid = GetParentProcessId(audioPid);
                                    if (parentPid != 0 && spotifyPids.Contains(parentPid)) isSpotifyAudio = true;
                                }
                            }

                            if (isSpotifyAudio)
                            {
                                var iidSimpleAudioVolume = new Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8");
                                hr = Marshal.QueryInterface(sessionControlPtr, ref iidSimpleAudioVolume,
                                    out var simpleVolumePtr);

                                if (hr == 0 && simpleVolumePtr != IntPtr.Zero)
                                    try
                                    {
                                        var simpleVolume =
                                            (ISimpleAudioVolume)Marshal.GetObjectForIUnknown(simpleVolumePtr);
                                        simpleVolume.SetMute(isMuted, Guid.Empty);

                                        Logger.Debug($"Set mute {isMuted}.");
                                    }
                                    finally
                                    {
                                        Marshal.Release(simpleVolumePtr);
                                    }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                        finally
                        {
                            if (sessionControlPtr != IntPtr.Zero) Marshal.Release(sessionControlPtr);
                        }
                    }
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    if (sessionEnumerator != null) Marshal.ReleaseComObject(sessionEnumerator);
                    if (sessionManagerObj != null) Marshal.ReleaseComObject(sessionManagerObj);
                    if (device != null) Marshal.ReleaseComObject(device);
                    if (devicePtr != IntPtr.Zero) Marshal.Release(devicePtr);
                }
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            if (devicesCollectionPtr != IntPtr.Zero) Marshal.Release(devicesCollectionPtr);
            if (deviceEnumerator != null) Marshal.ReleaseComObject(deviceEnumerator);
        }
    }

    private static uint GetParentProcessId(uint pid)
    {
        var hProcess = OpenProcess(0x0400, false, pid);
        if (hProcess == IntPtr.Zero) return 0;

        try
        {
            var pbi = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(hProcess, 0, ref pbi, Marshal.SizeOf(pbi), out var returnLength);
            if (status == 0) return (uint)pbi.InheritedFromUniqueProcessId.ToInt64();
        }
        catch
        {
            // ignored
        }
        finally
        {
            CloseHandle(hProcess);
        }

        return 0;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCountDelegate(IntPtr thisPtr, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ItemDelegate(IntPtr thisPtr, uint deviceIndex, out IntPtr devicePtr);


    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsContext, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(ref Guid audioSessionGuid, uint dwFlags, out IntPtr sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint dwFlags, out IntPtr audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionList);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetSession(int sessionCount, out IntPtr sessionControlPtr);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig]
        int GetState(out int pRetVal);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid pRetVal);

        [PreserveSig]
        int SetGroupingParam(ref Guid @override, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr newNotifications);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr newNotifications);

        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

        [PreserveSig]
        int GetProcessId(out uint pRetVal);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float fLevel, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolume(out float pfLevel);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}