using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace SpotHusher
{
    [Flags]
    public enum MemoryAreas
    {
        None = 0,
        CombinedPageList = 1,
        ModifiedPageList = 2,
        ProcessesWorkingSet = 4,
        StandbyList = 8,
        StandbyListLowPriority = 16,
        SystemWorkingSet = 32,
        ModifiedFileCache = 64,
        SystemFileCache = 128,
        RegistryCache = 256,

        Safe = ModifiedPageList | StandbyListLowPriority | SystemFileCache,
        Aggressive = ModifiedPageList | ProcessesWorkingSet | StandbyListLowPriority | SystemFileCache,
        Emergency = CombinedPageList | ModifiedPageList | ProcessesWorkingSet | StandbyList | StandbyListLowPriority | SystemFileCache,
        Desperate = CombinedPageList | ModifiedPageList | ProcessesWorkingSet | StandbyList | StandbyListLowPriority | SystemWorkingSet | ModifiedFileCache | SystemFileCache | RegistryCache
    }

    public static class MemoryOptimizer
    {
        public static string Optimize(MemoryAreas areas, NotifyIcon notifyIcon)
        {
            if (areas == MemoryAreas.None) return "No memory optimization areas selected.";

            var log = new StringBuilder();
            var stopwatch = new Stopwatch();
            var totalStopwatch = Stopwatch.StartNew();

            var (physBefore, virtBefore, _) = GetMemoryStatus();

            if ((areas & MemoryAreas.ProcessesWorkingSet) != 0)
            {
                ExecuteAction("Processes Working Set", () =>
                {
                    ElevatePrivilege("SeDebugPrivilege");
                    var currentProc = Path.GetFileNameWithoutExtension(Environment.ProcessPath);

                    foreach (var proc in Process.GetProcesses())
                    {
                        using (proc)
                        {
                            try
                            {
                                if (proc.ProcessName != currentProc &&
                                    proc.ProcessName != "Idle" &&
                                    proc.ProcessName != "System" &&
                                    proc.ProcessName != "Secure System")
                                    NativeMethods.EmptyWorkingSet(proc.Handle);
                            }
                            catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { }
                            catch
                            {
                                // ignored
                            }
                        }
                    }
                }, stopwatch, log);
            }

            if ((areas & MemoryAreas.SystemWorkingSet) != 0)
            {
                ExecuteAction("System Working Set", () =>
                {
                    ElevatePrivilege("SeIncreaseQuotaPrivilege");

                    if (Environment.Is64BitOperatingSystem)
                    {
                        var cacheInfo = new SystemCacheInformation64
                        {
                            MinimumWorkingSet = -1L,
                            MaximumWorkingSet = -1L
                        };

                        GCHandle handle = GCHandle.Alloc(cacheInfo, GCHandleType.Pinned);
                        try
                        {
                            ThrowIfNtFailed(NativeMethods.NtSetSystemInformation(21, handle.AddrOfPinnedObject(), Marshal.SizeOf(cacheInfo)));
                        }
                        finally { handle.Free(); }
                    }
                    else
                    {
                        var cacheInfo = new SystemCacheInformation32
                        {
                            MinimumWorkingSet = uint.MaxValue,
                            MaximumWorkingSet = uint.MaxValue
                        };

                        GCHandle handle = GCHandle.Alloc(cacheInfo, GCHandleType.Pinned);
                        try
                        {
                            ThrowIfNtFailed(NativeMethods.NtSetSystemInformation(21, handle.AddrOfPinnedObject(), Marshal.SizeOf(cacheInfo)));
                        }
                        finally { handle.Free(); }
                    }

                    IntPtr flushVal = IntPtr.Subtract(IntPtr.Zero, 1);
                    ThrowIfWin32Failed(NativeMethods.SetSystemFileCacheSize(flushVal, flushVal, 0));
                }, stopwatch, log);
            }

            if ((areas & MemoryAreas.ModifiedPageList) != 0)
            {
                ExecuteAction("Modified Page List", () =>
                {
                    ElevatePrivilege("SeProfileSingleProcessPrivilege");
                    int command = 3;
                    GCHandle handle = GCHandle.Alloc(command, GCHandleType.Pinned);
                    try
                    {
                        ThrowIfNtFailed(NativeMethods.NtSetSystemInformation(80, handle.AddrOfPinnedObject(), Marshal.SizeOf(command)));
                    }
                    finally { handle.Free(); }
                }, stopwatch, log);
            }

            if ((areas & (MemoryAreas.StandbyList | MemoryAreas.StandbyListLowPriority)) != 0)
            {
                ExecuteAction("Standby List", () =>
                {
                    ElevatePrivilege("SeProfileSingleProcessPrivilege");
                    int command = (areas & MemoryAreas.StandbyListLowPriority) != 0 ? 5 : 4;
                    GCHandle handle = GCHandle.Alloc(command, GCHandleType.Pinned);
                    try
                    {
                        ThrowIfNtFailed(NativeMethods.NtSetSystemInformation(80, handle.AddrOfPinnedObject(), Marshal.SizeOf(command)));
                    }
                    finally { handle.Free(); }
                }, stopwatch, log);
            }

            if ((areas & MemoryAreas.CombinedPageList) != 0)
            {
                ExecuteAction("Combined Page List", () =>
                {
                    ElevatePrivilege("SeProfileSingleProcessPrivilege");
                    var combineInfo = new MemoryCombineInformationEx();
                    GCHandle handle = GCHandle.Alloc(combineInfo, GCHandleType.Pinned);
                    try
                    {
                        ThrowIfNtFailed(NativeMethods.NtSetSystemInformation(130, handle.AddrOfPinnedObject(), Marshal.SizeOf(combineInfo)));
                    }
                    finally { handle.Free(); }
                }, stopwatch, log);
            }

            if ((areas & MemoryAreas.ModifiedFileCache) != 0)
            {
                ExecuteAction("Modified File Cache", () =>
                {
                    bool res;
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive == null || drive.DriveType != DriveType.Fixed || string.IsNullOrWhiteSpace(drive.Name))
                        {
                            continue;
                        }

                        using (var handle = OpenVolumeHandle(drive.Name))
                        {
                            if (handle == null || handle.IsInvalid)
                                continue;

                            try
                            {
                                var buffer = Marshal.AllocHGlobal(1);
                                try
                                {
                                    ThrowIfWin32Failed(NativeMethods.DeviceIoControl(handle, 589832,
                                        buffer, 1, IntPtr.Zero, 0, out _, IntPtr.Zero));
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal(buffer);
                                }

                                ThrowIfWin32Failed(NativeMethods.DeviceIoControl(handle, 589828,
                                    IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero));

                                ThrowIfWin32Failed(NativeMethods.FlushFileBuffers(handle));
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }
                }, stopwatch, log);
            }

            if ((areas & MemoryAreas.SystemFileCache) != 0)
            {
                ExecuteAction("System File Cache", () =>
                {
                    ElevatePrivilege("SeIncreaseQuotaPrivilege");

                    if (Environment.Is64BitOperatingSystem)
                    {
                        var cacheInfo = new SystemFileCacheInformation64
                        {
                            MinimumWorkingSet = -1L,
                            MaximumWorkingSet = -1L
                        };

                        GCHandle handle = GCHandle.Alloc(cacheInfo, GCHandleType.Pinned);
                        try
                        {
                            ThrowIfNtFailed(NativeMethods.NtSetSystemInformation(21, handle.AddrOfPinnedObject(), Marshal.SizeOf(cacheInfo)));
                        }
                        finally { handle.Free(); }
                    }
                    else
                    {
                        var cacheInfo = new SystemFileCacheInformation32
                        {
                            MinimumWorkingSet = int.MaxValue,
                            MaximumWorkingSet = int.MaxValue
                        };

                        GCHandle handle = GCHandle.Alloc(cacheInfo, GCHandleType.Pinned);
                        try
                        {
                            ThrowIfNtFailed(NativeMethods.NtSetSystemInformation(21, handle.AddrOfPinnedObject(), Marshal.SizeOf(cacheInfo)));
                        }
                        finally { handle.Free(); }
                    }

                    IntPtr flushVal = IntPtr.Subtract(IntPtr.Zero, 1);
                    ThrowIfWin32Failed(NativeMethods.SetSystemFileCacheSize(flushVal, flushVal, 0));
                }, stopwatch, log);
            }

            if ((areas & MemoryAreas.RegistryCache) != 0)
            {
                ExecuteAction("Registry Cache", () =>
                {
                    uint res = NativeMethods.NtSetSystemInformation(155, IntPtr.Zero, 0);
                    if (res != 0) throw new NativeStatusException(res);

                }, stopwatch, log);
            }

            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch
            {
                // ignored
            }

            totalStopwatch.Stop();
            var (physAfter, virtAfter, _) = GetMemoryStatus();

            long releasedPhysBytes = (long)physAfter - (long)physBefore;
            double releasedPhysGB = Math.Max(0.0, releasedPhysBytes / (1024.0 * 1024.0 * 1024.0));

            long releasedVirtBytes = (long)virtAfter - (long)virtBefore;
            double releasedVirtGB = Math.Max(0.0, releasedVirtBytes / (1024.0 * 1024.0 * 1024.0));

            //log.AppendLine("------------------------------------------------");
            if (releasedPhysGB <= 0.0 && releasedVirtGB <= 0.0)
            {
                log.AppendLine("No Optimization Needed.");
            }
            else
            {
                if (releasedPhysGB > 0.0)
                    log.AppendLine($"Physical: {releasedPhysGB:F2} GB");

                if (releasedVirtGB > 0.0)
                    log.AppendLine($"Virtual: {releasedVirtGB:F2} GB");

                log.AppendLine($"Time taken: {totalStopwatch.Elapsed.TotalSeconds:F2}s");
            }

            return log.ToString();
        }

        public static (ulong Phys, ulong Virt, uint MemoLoad) GetMemoryStatus()
        {
            var status = new MemoryStatusEx();
            status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            if (NativeMethods.GlobalMemoryStatusEx(ref status))
            {
                return (status.AvailPhys, status.AvailPageFile, status.MemoryLoad);
            }
            return (0, 0, 0);
        }

        private static void ExecuteAction(string name, Action action, Stopwatch sw, StringBuilder log)
        {
            sw.Restart();
            try
            {
                action();
                Logger.Debug($"{name} - {sw.Elapsed.TotalSeconds:F2}s.");
            }
            catch (Exception ex)
            {
                var msg = ex.Message;

                if (msg.Contains("0xC0000022") || msg.Contains("0xD0000005"))
                {
                    msg = msg.Contains("0xC0000022") ? msg.Replace("0xC0000022", "STATUS_ACCESS_DENIED") : msg.Replace("0xD0000005", "STATUS_ACCESS_DENIED");
                }
                else if (msg.Contains("0xC0000061"))
                {
                    msg = msg.Replace("0xC0000061", "STATUS_PRIVILEGE_NOT_HELD");
                }

                Logger.Error($"{name} - {sw.Elapsed.TotalSeconds:F2}s: {msg}.");
            }
        }

        private static void ElevatePrivilege(string privilegeName)
        {
            using (var current = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges))
            {
                TokenPrivileges newState;
                newState.Count = 1;
                newState.Luid = 0L;
                newState.Attr = 2;

                if (NativeMethods.LookupPrivilegeValue(null, privilegeName, ref newState.Luid))
                {
                    ThrowIfWin32Failed(NativeMethods.AdjustTokenPrivileges(current.Token, false, ref newState, 0,
                        IntPtr.Zero, IntPtr.Zero));
                }
                else
                {
                    throw NativeStatusException.FromWin32Error(Marshal.GetLastWin32Error());
                }
            }
        }

        private class NativeStatusException : Exception
        {
            public uint StatusCode { get; }

            public NativeStatusException(uint status) : base($"NTSTATUS Error: 0x{status:X8}")
            {
                StatusCode = status;
            }

            public static NativeStatusException FromWin32Error(int win32Error)
            {
                uint ntStatusFromWin32 = 0xD0000000 | (uint)win32Error;
                return new NativeStatusException(ntStatusFromWin32);
            }
        }

        private static void ThrowIfNtFailed(uint status)
        {
            if (status >= 0x80000000)
            {
                throw new NativeStatusException(status);
            }
        }

        private static void ThrowIfWin32Failed(bool success)
        {
            if (!success)
            {
                throw NativeStatusException.FromWin32Error(Marshal.GetLastWin32Error());
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SystemCacheInformation64
        {
            public long CurrentSize;
            public long PeakSize;
            public long PageFaultCount;
            public long MinimumWorkingSet;
            public long MaximumWorkingSet;
            public long Unused1;
            public long Unused2;
            public long Unused3;
            public long Unused4;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SystemCacheInformation32
        {
            public uint CurrentSize;
            public uint PeakSize;
            public uint PageFaultCount;
            public uint MinimumWorkingSet;
            public uint MaximumWorkingSet;
            public uint Unused1;
            public uint Unused2;
            public uint Unused3;
            public uint Unused4;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SystemFileCacheInformation32
        {
            public int CurrentSize;
            public int PeakSize;
            public int PageFaultCount;
            public int MinimumWorkingSet;
            public int MaximumWorkingSet;
            public int CurrentSizeIncludingTransitionInPages;
            public int PeakSizeIncludingTransitionInPages;
            public int TransitionRePurposeCount;
            public int Flags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SystemFileCacheInformation64
        {
            public long CurrentSize;
            public long PeakSize;
            public long PageFaultCount;
            public long MinimumWorkingSet;
            public long MaximumWorkingSet;
            public long CurrentSizeIncludingTransitionInPages;
            public long PeakSizeIncludingTransitionInPages;
            public long TransitionRePurposeCount;
            public long Flags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MemoryCombineInformationEx { public IntPtr Handle; public IntPtr Reserved; public IntPtr Flags; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TokenPrivileges { public int Count; public long Luid; public int Attr; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        private static class NativeMethods
        {
            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, ref long lpLuid);

            [DllImport("psapi.dll", SetLastError = true)]
            public static extern bool EmptyWorkingSet(IntPtr hProcess);

            [DllImport("ntdll.dll")]
            public static extern uint NtSetSystemInformation(int infoClass, IntPtr info, int length);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, int flags);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            internal static extern SafeFileHandle CreateFile([MarshalAs(UnmanagedType.LPWStr)] string lpFileName, FileAccess dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DeviceIoControl(SafeFileHandle hDevice, int dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

            [SuppressUnmanagedCodeSecurity]
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool FlushFileBuffers(SafeFileHandle hFile);
        }

        private static SafeFileHandle OpenVolumeHandle(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter))
                return null;
            return NativeMethods.CreateFile(
                @"\\.\" + driveLetter.TrimEnd(':', '\\') + ":",
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Write,
                IntPtr.Zero,
                FileMode.Open,
                (int)FileAttributes.Normal | 536870912,
                IntPtr.Zero
            );
        }
    }
}