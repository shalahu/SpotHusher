using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpotHusher;

public class SpotifyHookMonitor : IDisposable
{
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectNamechange = 0x800C;
    private const uint WineventOutofcontext = 0;
    private const uint WmAppcommand = 0x0319;
    private IntPtr _lifecycleHookHandle = IntPtr.Zero;
    private IntPtr _nameChangeHookHandle = IntPtr.Zero;
    private WinEventDelegate? _procDelegate;
    private bool _isSpotifyRunning = true;

    public Action<string>? OnPlaybackChanged;

    public void Dispose()
    {
        if (_nameChangeHookHandle != IntPtr.Zero) UnhookWinEvent(_nameChangeHookHandle);
        if (_lifecycleHookHandle != IntPtr.Zero) UnhookWinEvent(_lifecycleHookHandle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public bool IsSpotifyHiddenInTray()
    {
        try
        {
            var processes = Process.GetProcessesByName("Spotify");
            if (processes.Length == 0) return true;

            var hwnd = processes[0].MainWindowHandle;
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return true;

            var hasActiveMain = false;
            foreach (var p in processes)
            {
                var checkHwnd = IntPtr.Zero;
                while ((checkHwnd = FindWindowEx(IntPtr.Zero, checkHwnd, null, null)) != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(checkHwnd, out var pid);
                    if (pid != p.Id) continue;

                    var cls = new StringBuilder(256);
                    GetClassName(checkHwnd, cls, cls.Capacity);
                    var ttl = new StringBuilder(512);
                    GetWindowText(checkHwnd, ttl, ttl.Capacity);

                    if (cls.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(ttl.ToString().Trim()))
                    {
                        hasActiveMain = true;
                        break;
                    }
                }

                if (hasActiveMain) break;
            }

            return !hasActiveMain;
        }
        catch
        {
            return false;
        }
    }

    private IntPtr FindSpotifyCoreWindow()
    {
        try
        {
            var processes = Process.GetProcessesByName("Spotify");
            if (processes.Length == 0) return IntPtr.Zero;

            var isHidden = IsSpotifyHiddenInTray();
            var backupHwnd = IntPtr.Zero;

            foreach (var p in processes)
            {
                var hwnd = IntPtr.Zero;
                while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, null, null)) != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid != p.Id) continue;

                    var cls = new StringBuilder(256);
                    GetClassName(hwnd, cls, cls.Capacity);
                    var ttl = new StringBuilder(512);
                    GetWindowText(hwnd, ttl, ttl.Capacity);
                    var title = ttl.ToString().Trim();

                    if (!isHidden &&
                        cls.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(title)) return hwnd;

                    if (backupHwnd == IntPtr.Zero && IsTitleValid(title)) backupHwnd = hwnd;
                }
            }

            return backupHwnd;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private bool IsTitleValid(string title)
    {
        return !string.IsNullOrEmpty(title) &&
               !title.StartsWith("Default", StringComparison.OrdinalIgnoreCase) &&
               !title.EndsWith("UI", StringComparison.OrdinalIgnoreCase) &&
               !title.Contains("HintWnd", StringComparison.OrdinalIgnoreCase);
    }

    public string FetchCurrentTitle()
    {
        var hwnd = FindSpotifyCoreWindow();
        if (hwnd != IntPtr.Zero)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);

            var title = sb.ToString().Trim();
            Logger.Debug($"Got title {title}.");

            if (!string.IsNullOrEmpty(title))
            {
                Logger.Debug($"Return title {title}.");
                return title;
            }
        }

        return "Spotify is not running";
    }

    public void PlayPause()
    {
        SendTargetedCommand(14);
    }

    public void NextTrack()
    {
        SendTargetedCommand(11);
    }

    public void PreviousTrack()
    {
        SendTargetedCommand(12);
    }

    private void SendTargetedCommand(long cmd)
    {
        var hwnd = FindSpotifyCoreWindow();
        if (hwnd != IntPtr.Zero)
            PostMessage(hwnd, WmAppcommand, IntPtr.Zero, (IntPtr)(cmd << 16));
    }

    public void Start()
    {
        if (_nameChangeHookHandle != IntPtr.Zero) return;
        _procDelegate = WinEventProc;
        _nameChangeHookHandle = SetWinEventHook(EventObjectNamechange, EventObjectNamechange, IntPtr.Zero,
            _procDelegate, 0, 0, WineventOutofcontext);
        _lifecycleHookHandle = SetWinEventHook(EventObjectDestroy, EventObjectShow, IntPtr.Zero, _procDelegate, 0,
            0, WineventOutofcontext);
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0) return;
        if (eventType == EventObjectDestroy && _isSpotifyRunning)
        {
            if (Process.GetProcessesByName("Spotify").Length == 0)
            {
                _isSpotifyRunning = false;

                OnPlaybackChanged?.Invoke("Spotify is not running");
            }

            return;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return;

        try
        {
            var proc = Process.GetProcessById((int)pid);
            if (proc.ProcessName.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
            {
                if (eventType == EventObjectShow) Thread.Sleep(200);

                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, cls.Capacity);
                var sb = new StringBuilder(512);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString().Trim();

                Logger.Debug($"Got title {title}.");

                var isValid = false;
                var isHidden = IsSpotifyHiddenInTray();

                if (!isHidden)
                {
                    if (cls.ToString().StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(title) || title.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase))
                        isValid = true;
                }
                else
                {
                    if (IsTitleValid(title))
                        isValid = true;
                }

                if (isValid && !string.IsNullOrEmpty(title))
                {
                    Logger.Debug($"Return title {title}.");
                    OnPlaybackChanged?.Invoke(title);
                }

                _isSpotifyRunning = true;
            }
        }
        catch
        {
            // ignored
        }
    }

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);
}