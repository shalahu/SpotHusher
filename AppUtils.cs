using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpotHusher;

public static class Logger
{
    private static readonly object ExternalLogger;
    private static readonly MethodInfo InfoMethod;
    private static readonly MethodInfo ErrorMethod;
    private static readonly MethodInfo DebugMethod;

    static Logger()
    {
        try
        {
            var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NLog.dll");
            if (!File.Exists(dllPath)) return;

            var assembly = Assembly.LoadFrom(dllPath);

            var configType = assembly.GetType("NLog.Config.LoggingConfiguration");
            var fileTargetType = assembly.GetType("NLog.Targets.FileTarget");
            var targetType = assembly.GetType("NLog.Targets.Target");
            var logLevelType = assembly.GetType("NLog.LogLevel");
            var logManagerType = assembly.GetType("NLog.LogManager");
            var loggingRuleType = assembly.GetType("NLog.Config.LoggingRule");

            if (configType == null || fileTargetType == null || logManagerType == null || loggingRuleType == null) return;

            var config = Activator.CreateInstance(configType);
            var fileTarget = Activator.CreateInstance(fileTargetType);

            var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "spothusher.log");

            var nameProp = fileTargetType.GetProperty("Name");
            var fileNameProp = fileTargetType.GetProperty("FileName");
            var layoutProp = fileTargetType.GetProperty("Layout");

            if (nameProp == null || fileNameProp == null || layoutProp == null) return;

            nameProp.SetValue(fileTarget, "logfile");

            var layoutConstructor = assembly.GetType("NLog.Layouts.SimpleLayout")?.GetConstructor([typeof(string)]);
            if (layoutConstructor == null) return;

            fileNameProp.SetValue(fileTarget, layoutConstructor.Invoke([logFilePath]));
            layoutProp.SetValue(fileTarget, layoutConstructor.Invoke(["${longdate} [${level:uppercase=true}] ${message}"]));

            var archiveAboveSizeProp = fileTargetType.GetProperty("ArchiveAboveSize");
            var maxArchiveFilesProp = fileTargetType.GetProperty("MaxArchiveFiles");
            var archiveSuffixFormatProp = fileTargetType.GetProperty("ArchiveSuffixFormat");

            archiveAboveSizeProp?.SetValue(fileTarget, 10485760L);

            maxArchiveFilesProp?.SetValue(fileTarget, 30);

            archiveSuffixFormatProp?.SetValue(fileTarget, ".{0:D2}");

            var addTargetMethod = configType.GetMethod("AddTarget", [typeof(string), targetType]);
            addTargetMethod?.Invoke(config, ["logfile", fileTarget]);

            var fromOrdinalMethod = logLevelType?.GetMethod("FromOrdinal", BindingFlags.Public | BindingFlags.Static);
            var debugLevel = fromOrdinalMethod?.Invoke(null, [1]);

            var ruleConstructor = loggingRuleType.GetConstructor([typeof(string), logLevelType, targetType]);
            if (ruleConstructor != null)
            {
                var rule = ruleConstructor.Invoke(["*", debugLevel, fileTarget]);

                var loggingRulesProp = configType.GetProperty("LoggingRules", BindingFlags.Public | BindingFlags.Instance);
                var loggingRulesCollection = loggingRulesProp?.GetValue(config);
                var addRuleMethod = loggingRulesCollection?.GetType().GetMethod("Add");
                addRuleMethod?.Invoke(loggingRulesCollection, [rule]);
            }

            var setConfigMethod = logManagerType.GetMethod("set_Configuration", [configType]);
            setConfigMethod?.Invoke(null, [config]);

            var reconfigMethod = logManagerType.GetMethod("ReconfigExistingLoggers", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            reconfigMethod?.Invoke(null, null);

            var getLoggerMethod = logManagerType.GetMethod("GetCurrentClassLogger", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (getLoggerMethod == null) return;

            ExternalLogger = getLoggerMethod.Invoke(null, null);

            if (ExternalLogger != null)
            {
                var loggerType = ExternalLogger.GetType();
                InfoMethod = loggerType.GetMethod("Info", BindingFlags.Public | BindingFlags.Instance, null, [typeof(string)], null);
                ErrorMethod = loggerType.GetMethod("Error", BindingFlags.Public | BindingFlags.Instance, null, [typeof(string)], null);
                DebugMethod = loggerType.GetMethod("Debug", BindingFlags.Public | BindingFlags.Instance, null, [typeof(string)], null);

                if (InfoMethod == null || ErrorMethod == null || DebugMethod == null)
                {
                    ExternalLogger = null;
                }
            }
        }
        catch
        {
            ExternalLogger = null;
        }
    }

    public static void Info(string message, [CallerMemberName] string callerName = "")
    {
        if (ExternalLogger != null && InfoMethod != null)
        {
            try
            {
                var declaringType = new StackFrame(1).GetMethod()?.DeclaringType;

                var fullName = declaringType != null
                    ? $"{declaringType.FullName}.{callerName}"
                    : callerName;

                InfoMethod.Invoke(ExternalLogger, [$" [{fullName}] {message}"]);
            }
            catch
            {
                // ignored
            }
        }
    }

    public static void Error(string message, [CallerMemberName] string callerName = "")
    {
        if (ExternalLogger != null && ErrorMethod != null)
        {
            try
            {
                var declaringType = new StackFrame(1).GetMethod()?.DeclaringType;

                var fullName = declaringType != null
                    ? $"{declaringType.FullName}.{callerName}"
                    : callerName;

                ErrorMethod.Invoke(ExternalLogger, [$" [{fullName}] {message}"]);
            }
            catch
            {
                // ignored
            }
        }
    }

    public static void Debug(string message, [CallerMemberName] string callerName = "")
    {
        if (ExternalLogger != null && DebugMethod != null)
        {
            try
            {
                var declaringType = new StackFrame(1).GetMethod()?.DeclaringType;

                var fullName = declaringType != null
                    ? $"{declaringType.FullName}.{callerName}"
                    : callerName;

                DebugMethod.Invoke(ExternalLogger, [$" [{fullName}] {message}"]);
            }
            catch
            {
                // ignored
            }
        }
    }
}

public static class ResourceLoader
{
    public static Icon LoadEmbeddedIcon(string resourceName, Icon defaultIcon)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                return new Icon(stream);
            }
        }

        return defaultIcon;
    }
}

public static class ShortcutCreator
{
    public static void CreateDesktopShortcut(NotifyIcon trayIcon)
    {
        try
        {
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath)) return;

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutLocation = Path.Combine(desktopPath, "SpotHusher.lnk");

            var wshShellType = Type.GetTypeFromProgID("WScript.Shell");
            if (wshShellType == null) return;

            dynamic shell = Activator.CreateInstance(wshShellType)!;
            var shortcut = shell.CreateShortcut(shortcutLocation);

            shortcut.TargetPath = currentExePath;
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Description = "A lightweight, zero-injection Windows utility that automatically mutes or skips Spotify advertisements by leveraging Win32 hooks and Core Audio COM interfaces.";
            shortcut.IconLocation = currentExePath + ",0";

            shortcut.Save();

            trayIcon.ShowBalloonTip(3000, "Shortcut created 🔧", "SpotHusher shortcut added to desktop.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            trayIcon.ShowBalloonTip(3000, "Shortcut creation failed ⚠️", $"Error adding SpotHusher to desktop: {ex.Message}", ToolTipIcon.Warning);
        }
    }
}

public static class IconFactory
{
    private static IntPtr _currentDynamicHIcon = IntPtr.Zero;

    public static void Clear([CallerMemberName] string callerName = "")
    {
        if (_currentDynamicHIcon != IntPtr.Zero)
        {
            DestroyIcon(_currentDynamicHIcon);
            Logger.Debug($"{_currentDynamicHIcon} - {Thread.CurrentThread.ManagedThreadId} - {callerName}");
            _currentDynamicHIcon = IntPtr.Zero;
        }
    }

    public static void UpdateIcon(int number, NotifyIcon notifyIcon)
    {
        var bitmap = CreateTrayBitmap(number);

        var hIcon = bitmap.GetHicon();
        Logger.Debug($"{hIcon} - {number}");
        var newIcon = Icon.FromHandle(hIcon);

        notifyIcon.Icon = newIcon;

        Clear();

        _currentDynamicHIcon = hIcon;

        bitmap.Dispose();
    }

    public static Bitmap CreateTrayBitmap(int percentage)
    {
        var iconSize = SystemInformation.SmallIconSize.Width;
        var bmp = new Bitmap(iconSize, iconSize);

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

            Brush bgBrush;
            if (percentage >= 90) bgBrush = new SolidBrush(Color.FromArgb(236, 28, 36));
            else if (percentage >= 70) bgBrush = new SolidBrush(Color.FromArgb(225, 128, 64));
            else bgBrush = new SolidBrush(Color.FromArgb(0, 128, 64));

            g.FillRectangle(bgBrush, 0, 0, iconSize, iconSize);
            bgBrush.Dispose();

            var text = percentage.ToString();
            if (percentage > 99) text = "99";

            float fontEmSize;
            int yOffset;

            switch (iconSize)
            {
                case 16:
                    fontEmSize = 4.0f;
                    yOffset = 1;
                    break;
                case 20:
                    fontEmSize = 5.0f;
                    yOffset = 1;
                    break;
                case 24:
                    fontEmSize = 6.0f;
                    yOffset = 2;
                    break;
                case 32:
                    fontEmSize = 8.0f;
                    yOffset = 2;
                    break;
                default:
                    fontEmSize = iconSize / 4.0f;
                    yOffset = (int)Math.Round(iconSize / 16f);
                    break;
            }

            using (var font = new Font("Lucida Console", fontEmSize, FontStyle.Regular))
            {
                var flags = TextFormatFlags.NoPadding |
                            TextFormatFlags.HorizontalCenter |
                            TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoClipping;

                var rect = new Rectangle(0, yOffset, iconSize, iconSize);

                TextRenderer.DrawText(g, text, font, rect, Color.White, flags);
            }
        }

        return bmp;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}

public class AppLifecycleManager : IDisposable
{
    private const string CrashFlagPath = "app.running";

    public bool IsCrashFlagExists;

    public AppLifecycleManager()
    {
        IsCrashFlagExists = File.Exists(CrashFlagPath);

        if (!IsCrashFlagExists)
        {
            try
            {
                File.Create(CrashFlagPath);
            }
            catch
            {
                // ignored
            }
        }
    }

    private void Clean()
    {
        if (File.Exists(CrashFlagPath))
        {
            try
            {
                File.Delete(CrashFlagPath);
            }
            catch
            {
                // ignored
            }
        }
    }

    public void Dispose()
    {
        Clean();
    }
}

public static class Extensions
{
    public static string ToFriendlyString(this long totalSeconds)
    {
        var t = TimeSpan.FromSeconds(totalSeconds);

        string FormatUnit(int value, string unit) => $"{value} {unit}{(value == 1 ? "" : "s")}";

        if (t.Days > 0)
        {
            return $"{FormatUnit(t.Days, "Day")} {FormatUnit(t.Hours, "Hour")} {FormatUnit(t.Minutes, "Minute")} {FormatUnit(t.Seconds, "Second")}";
        }

        if (t.Hours > 0)
        {
            return $"{FormatUnit(t.Hours, "Hour")} {FormatUnit(t.Minutes, "Minute")} {FormatUnit(t.Seconds, "Second")}";
        }

        if (t.Minutes > 0)
        {
            return $"{FormatUnit(t.Minutes, "Minute")} {FormatUnit(t.Seconds, "Second")}";
        }

        return $"{FormatUnit(t.Seconds, "Second")}";
    }
}

public static class WindowsPowerManager
{
    private const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
    private const int TOKEN_QUERY = 0x00000008;
    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
    private const int SE_PRIVILEGE_ENABLED = 0x00000002;

    private const uint EWX_LOGOFF = 0x00000000;
    private const uint EWX_SHUTDOWN = 0x00000001;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_FORCE = 0x00000004;
    private const uint EWX_POWEROFF = 0x00000008;

    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, ref IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, ref LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private static bool EnableShutdownPrivilege()
    {
        var hToken = IntPtr.Zero;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref hToken))
        {
            return false;
        }

        try
        {
            var luid = new LUID();
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, ref luid))
            {
                return false;
            }

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                }
            };

            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                return false;
            }

            return Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            if (hToken != IntPtr.Zero)
            {
                CloseHandle(hToken);
            }
        }
    }

    private static bool ConfirmAction(string actionName)
    {
        var result = MessageBox.Show(
            $"Do you really want to {actionName}?", 
            "System Power", 
            MessageBoxButtons.YesNo, 
            MessageBoxIcon.Question);

        return result == DialogResult.Yes;
    }

    public static bool Shutdown()
    {
        if (!ConfirmAction("shutdown")) return false;

        if (EnableShutdownPrivilege())
        {
            return ExitWindowsEx(EWX_SHUTDOWN | EWX_POWEROFF | EWX_FORCE, SHTDN_REASON_FLAG_PLANNED);
        }
        return false;
    }

    public static bool Restart()
    {
        if (!ConfirmAction("restart")) return false;

        if (EnableShutdownPrivilege())
        {
            return ExitWindowsEx(EWX_REBOOT | EWX_FORCE, SHTDN_REASON_FLAG_PLANNED);
        }
        return false;
    }

    public static bool Logoff()
    {
        if (!ConfirmAction("logoff")) return false;

        return ExitWindowsEx(EWX_LOGOFF | EWX_FORCE, 0);
    }

    public static bool Lock()
    {
        if (!ConfirmAction("lock the computer")) return false;

        return LockWorkStation();
    }

    public static bool StandBy()
    {
        if (!ConfirmAction("stand by")) return false;

        return SetSuspendState(false, true, true);
    }

    public static bool Hibernate()
    {
        if (!ConfirmAction("hibernate")) return false;

        return SetSuspendState(true, true, true);
    }
}