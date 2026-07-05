using AudioSwitcher.AudioApi.CoreAudio;
using Microsoft.Win32;
using SpotHusher;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using (_ = new Mutex(true, "Global\\SpotHusher_SingleInstance_Mutex_Key", out var isNewInstance))
{
    if (!isNewInstance) return;

    ApplicationConfiguration.Initialize();

    var appContext = new BackgroundAppContext();
    Application.Run(appContext);
}

public class SpotHusherConfig
{
    public bool AutoSkipAdViaRestartEnabled { get; set; }
    public bool AutoLaunchSpotifyEnabled { get; set; }
    public bool AutoPausePlaybackEnabled { get; set; }
}

public class BackgroundAppContext : ApplicationContext
{
    private static readonly string JsonConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    private readonly ToolStripMenuItem _autoLaunchItem;
    private readonly ToolStripMenuItem _autoSkipAdItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _autoPauseItem;
    private readonly ToolStripMenuItem _husherItem;
    private readonly ToolStripMenuItem _launchItem;
    private readonly SpotifyHookMonitor _monitor;
    private readonly Icon _muteIcon;
    private readonly Icon _pausedIcon;
    private readonly Icon _playIcon;
    private readonly Icon _disabledIcon;
    private readonly Icon _notRunningIcon;
    private readonly ToolStripMenuItem _playPauseItem;
    private readonly ToolStripMenuItem _shortcutItem;

    private readonly ToolStripMenuItem _trackItem;
    private readonly NotifyIcon _trayIcon;
    private bool _currentAdState;
    private string _currentTrackTitle;

    private List<CoreAudioDevice> _devices = [];
    private bool _isSpotifyPlaying;
    private bool _isUserPaused;
    private bool _wasHiddenBefore;
    private bool _autoPlayAfterLaunch;
    private bool _playNext;

    public BackgroundAppContext()
    {
        _playIcon = ResourceLoader.LoadEmbeddedIcon($"{nameof(SpotHusher)}.Resources.SpotHusher_play.ico", SystemIcons.Shield);
        _muteIcon = ResourceLoader.LoadEmbeddedIcon($"{nameof(SpotHusher)}.Resources.SpotHusher_mute.ico", SystemIcons.Error);
        _pausedIcon =
            ResourceLoader.LoadEmbeddedIcon($"{nameof(SpotHusher)}.Resources.SpotHusher_paused.ico", SystemIcons.Warning);
        _disabledIcon =
            ResourceLoader.LoadEmbeddedIcon($"{nameof(SpotHusher)}.Resources.SpotHusher_disabled.ico", SystemIcons.Hand);
        _notRunningIcon =
            ResourceLoader.LoadEmbeddedIcon($"{nameof(SpotHusher)}.Resources.SpotHusher_not_running.ico", SystemIcons.Information);

        var contextMenu = new ContextMenuStrip();

        _monitor = new SpotifyHookMonitor();
        _monitor.OnPlaybackChanged = title =>
        {
            if (_trayIcon.ContextMenuStrip.InvokeRequired)
                _trayIcon.ContextMenuStrip.BeginInvoke(() => Task.Run(async () => await ProcessSpotifyTitle(title)));
            else
                Task.Run(async () => await ProcessSpotifyTitle(title));
        };

        _monitor.Start();

        _currentTrackTitle = _monitor.FetchCurrentTitle();

        _trackItem = new ToolStripMenuItem($"🎵 Playing: {_currentTrackTitle}") { Enabled = false };

        _launchItem = new ToolStripMenuItem("🚀 Launch Spotify", null, (s, e) => { LaunchSpotifyClient(false); })
        { Visible = false };

        _playPauseItem = new ToolStripMenuItem("⏯️ Play / Resume Playback", null, (s, e) => { PlayPause(); })
        { Visible = false };

        void PlayPause()
        {
            if (!_currentAdState)
                try
                {
                    _monitor.PlayPause();
                }
                catch (Exception ex)
                {
                    _trayIcon.ShowBalloonTip(3000, "Failed to send ⚠️",
                        $"Error sending {(_isSpotifyPlaying ? "Pause Playback" : "Resume Playback")}: {ex.Message}",
                        ToolTipIcon.Warning);
                }
        }

        _autoSkipAdItem = new ToolStripMenuItem("⚡ Auto-Skip Ads via Restart (May Cause Screen Flash)", null, (s, e) =>
        {
            IsAutoSkipAdViaRestartEnabled = !IsAutoSkipAdViaRestartEnabled;
            _autoSkipAdItem.Checked = IsAutoSkipAdViaRestartEnabled;

            if (IsAutoSkipAdViaRestartEnabled && _currentAdState && !_isUserPaused)
            {
                Task.Run(async () => await ExecuteForceSkipAd());
            }
        });

        _autoLaunchItem = new ToolStripMenuItem(" ▶️ Auto-Launch Spotify With SpotHusher", null, (s, e) =>
        {
            IsAutoLaunchSpotifyEnabled = !IsAutoLaunchSpotifyEnabled;
            _autoLaunchItem.Checked = IsAutoLaunchSpotifyEnabled;
        });

        _autoPauseItem = new ToolStripMenuItem("⏸️ Auto-Pause Spotify On Lock & Sleep", null, (s, e) =>
        {
            IsAutoPausePlaybackEnabled = !IsAutoPausePlaybackEnabled;
            _autoPauseItem.Checked = IsAutoPausePlaybackEnabled;

            if (IsAutoPausePlaybackEnabled)
            {
                AddAutoPausePlaybackEvent();
            }
            else
            {
                RemoveAutoPausePlaybackEvent();
            }
        });

        var audioDevicesMenu = new ToolStripMenuItem("🎧 Switch Audio Output");

        _shortcutItem = new ToolStripMenuItem("📌 Create Desktop Shortcut", null, (s, e) =>
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutLinkPath = Path.Combine(desktopPath, "SpotHusher.lnk");
            if (_shortcutItem.Checked)
            {
                try
                {
                    if (File.Exists(shortcutLinkPath)) File.Delete(shortcutLinkPath);
                    _shortcutItem.Checked = false;
                    _shortcutItem.Text = "📌 Create Desktop Shortcut";
                    _trayIcon.ShowBalloonTip(3000, "Shortcut removed 🔧", "SpotHusher shortcut removed from desktop.",
                        ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    _trayIcon.ShowBalloonTip(4000, "Failed to remove shortcut ⚠️",
                        $"Error removing shorcut from desktop: {ex.Message}", ToolTipIcon.Warning);
                }
            }
            else
            {
                ShortcutCreator.CreateDesktopShortcut(_trayIcon);
                var isExist = File.Exists(shortcutLinkPath);
                _shortcutItem.Checked = isExist;
            }
        });

        _autoStartItem = new ToolStripMenuItem("⚙ Run at Windows Startup", null, (s, e) =>
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (_autoStartItem.Checked)
                    {
                        key.DeleteValue("SpotHusher", false);
                        _autoStartItem.Checked = false;
                        _trayIcon.ShowBalloonTip(3000, "Removed from startup 🚀", "SpotHusher removed from startup.",
                            ToolTipIcon.Info);
                    }
                    else
                    {
                        var currentExePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(currentExePath))
                        {
                            key.SetValue("SpotHusher", $"\"{currentExePath}\"");
                            _autoStartItem.Checked = true;
                            _trayIcon.ShowBalloonTip(3000, "Added to startup 🚀", "SpotHusher added to startup.",
                                ToolTipIcon.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _trayIcon.ShowBalloonTip(3000, "Failed to add SpotHusher to startup ⚠️",
                    $"Error adding SpotHusher to startup: {ex.Message}", ToolTipIcon.Warning);
            }
        });

        _husherItem = new ToolStripMenuItem("🚫 Disable SpotHusher", null, (s, e) =>
        {
            _isUserPaused = !_isUserPaused;
            if (_isUserPaused)
            {
                _husherItem.Text = "🛡️ Enable SpotHusher";
                Task.Run(async () => await SpotifyAudioController.SetMute(false));
            }
            else
            {
                _husherItem.Text = "🚫 Disable SpotHusher";
                Task.Run(async () => await SpotifyAudioController.SetMute(_currentAdState));

                if (IsAutoSkipAdViaRestartEnabled && _currentAdState)
                {
                    Task.Run(async () => await ExecuteForceSkipAd());
                }
            }

            UpdateTrayUi(_currentAdState);
        });

        var exitItem = new ToolStripMenuItem("⏻  Exit", null, (s, e) =>
        {
            Task.Run(async () => await SpotifyAudioController.SetMute(false));
            SpotifyAudioController.Dispose();

            RemoveAutoPausePlaybackEvent();

            _trayIcon.Visible = false;

            _trayIcon.Dispose();
            _playIcon.Dispose();
            _muteIcon.Dispose();
            _pausedIcon.Dispose();
            _disabledIcon.Dispose();
            _notRunningIcon.Dispose();
            _monitor.Dispose();

            Application.Exit();
        });

        contextMenu.Opening += (s, e) =>
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                           false))
                {
                    if (key != null) _autoStartItem.Checked = key.GetValue("SpotHusher") != null;
                }

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                _shortcutItem.Checked = File.Exists(Path.Combine(desktopPath, "SpotHusher.lnk"));
                _autoSkipAdItem.Checked = IsAutoSkipAdViaRestartEnabled;
                _autoLaunchItem.Checked = IsAutoLaunchSpotifyEnabled;
                _autoPauseItem.Checked = IsAutoPausePlaybackEnabled;

                if (_currentTrackTitle != AppDefs.SpotifyNotRunning) UpdateSpotifyStatus();

                if (audioDevicesMenu.DropDownItems.Count == 0)
                    switch (_devices)
                    {
                        case null:
                            return;

                        case { Count: 0 }:
                            {
                                var item = new ToolStripMenuItem("No audio devices found") { Enabled = false };
                                audioDevicesMenu.DropDownItems.Add(item);

                                return;
                            }
                    }

                foreach (var device in _devices)
                {
                    var deviceItem = new ToolStripMenuItem(device.FullName);

                    if (device.IsDefaultDevice)
                    {
                        deviceItem.Checked = true;
                        deviceItem.Font = new Font(deviceItem.Font, FontStyle.Bold);
                    }

                    deviceItem.Click += async (s, ev) =>
                    {
                        foreach (ToolStripMenuItem item in audioDevicesMenu.DropDownItems)
                        {
                            item.Checked = false;
                            item.Font = new Font(deviceItem.Font, FontStyle.Regular);
                        }

                        deviceItem.Checked = true;
                        deviceItem.Font = new Font(deviceItem.Font, FontStyle.Bold);

                        if (await SpotifyAudioController.SetDefaultPlaybackDevice(device.Id))
                            _trayIcon.ShowBalloonTip(2000, "Audio output switched 🎧", $"Switched to: {device.FullName}.",
                                ToolTipIcon.Info);
                    };

                    if (audioDevicesMenu.DropDownItems.Count < _devices.Count)
                        audioDevicesMenu.DropDownItems.Add(deviceItem);
                }
            }
            catch
            {
                // ignored
            }
        };

        contextMenu.Items.Add(_trackItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_launchItem);
        contextMenu.Items.Add(_playPauseItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_autoSkipAdItem);
        contextMenu.Items.Add(_autoLaunchItem);
        contextMenu.Items.Add(_autoPauseItem);
        contextMenu.Items.Add(audioDevicesMenu);
        contextMenu.Items.Add(_shortcutItem);
        contextMenu.Items.Add(_autoStartItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_husherItem);
        contextMenu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        { Icon = _playIcon, ContextMenuStrip = contextMenu, Text = "Connecting Spotify...", Visible = true };
        _trayIcon.DoubleClick += (s, e) => { PlayPause(); };

        Task.Run(async () => await ProcessSpotifyTitle(_currentTrackTitle));

        Task.Run(async () => _devices = await SpotifyAudioController.GetActiveDevices());

        if (IsAutoLaunchSpotifyEnabled)
        {
            LaunchSpotifyClient(false);
        }

        if (IsAutoPausePlaybackEnabled)
        {
            AddAutoPausePlaybackEvent();
        }
        return;
        void AddAutoPausePlaybackEvent()
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        void RemoveAutoPausePlaybackEvent()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:

                if (_isSpotifyPlaying)
                {
                    _monitor.PlayPause();
                }

                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:

                if (_isSpotifyPlaying)
                {
                    _monitor.PlayPause();
                }

                break;
        }
    }

    private bool IsAutoSkipAdViaRestartEnabled
    {
        get
        {
            try
            {
                if (File.Exists(JsonConfigPath))
                {
                    var jsonString = File.ReadAllText(JsonConfigPath);
                    var config = JsonSerializer.Deserialize<SpotHusherConfig>(jsonString);
                    return config != null && config.AutoSkipAdViaRestartEnabled;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }
        set
        {
            try
            {
                SpotHusherConfig config;
                if (File.Exists(JsonConfigPath))
                {
                    var jsonString = File.ReadAllText(JsonConfigPath);
                    config = JsonSerializer.Deserialize<SpotHusherConfig>(jsonString) ?? new SpotHusherConfig();
                }
                else
                {
                    config = new SpotHusherConfig();
                }

                config.AutoSkipAdViaRestartEnabled = value;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = JsonSerializer.Serialize(config, options);
                File.WriteAllText(JsonConfigPath, updatedJson);
            }
            catch
            {
                // ignored
            }
        }
    }

    private bool IsAutoLaunchSpotifyEnabled
    {
        get
        {
            try
            {
                if (File.Exists(JsonConfigPath))
                {
                    var jsonString = File.ReadAllText(JsonConfigPath);
                    var config = JsonSerializer.Deserialize<SpotHusherConfig>(jsonString);
                    return config != null && config.AutoLaunchSpotifyEnabled;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }
        set
        {
            try
            {
                SpotHusherConfig config;
                if (File.Exists(JsonConfigPath))
                {
                    var jsonString = File.ReadAllText(JsonConfigPath);
                    config = JsonSerializer.Deserialize<SpotHusherConfig>(jsonString) ?? new SpotHusherConfig();
                }
                else
                {
                    config = new SpotHusherConfig();
                }

                config.AutoLaunchSpotifyEnabled = value;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = JsonSerializer.Serialize(config, options);
                File.WriteAllText(JsonConfigPath, updatedJson);
            }
            catch
            {
                // ignored
            }
        }
    }

    private bool IsAutoPausePlaybackEnabled
    {
        get
        {
            try
            {
                if (File.Exists(JsonConfigPath))
                {
                    var jsonString = File.ReadAllText(JsonConfigPath);
                    var config = JsonSerializer.Deserialize<SpotHusherConfig>(jsonString);
                    return config != null && config.AutoPausePlaybackEnabled;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }
        set
        {
            try
            {
                SpotHusherConfig config;
                if (File.Exists(JsonConfigPath))
                {
                    var jsonString = File.ReadAllText(JsonConfigPath);
                    config = JsonSerializer.Deserialize<SpotHusherConfig>(jsonString) ?? new SpotHusherConfig();
                }
                else
                {
                    config = new SpotHusherConfig();
                }

                config.AutoPausePlaybackEnabled = value;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = JsonSerializer.Serialize(config, options);
                File.WriteAllText(JsonConfigPath, updatedJson);
            }
            catch
            {
                // ignored
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private async Task ExecuteForceSkipAd()
    {
        try
        {
            _wasHiddenBefore = _monitor.IsSpotifyHiddenInTray();

            await SpotifyAudioController.SetMute(false);

            var processes = Process.GetProcessesByName(AppDefs.SpotifyProcessName);

            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    await p.WaitForExitAsync();
                }
                catch
                {
                    // ignored
                }

                p.Dispose();
            }

            LaunchSpotifyClient(true);
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "Failed to skip ad ⚠️", $"Error skipping ad: {ex.Message}",
                ToolTipIcon.Warning);
        }
    }

    private async Task ProcessSpotifyTitle(string title)
    {
        Logger.Debug($"Got title {title}.");

        var isSpotifyReady = title.StartsWith(AppDefs.SpotifyIsReadyWindowsClassNamePrefix);

        if (isSpotifyReady && _autoPlayAfterLaunch)
        {
            if (_wasHiddenBefore)
            {
                var freshProcs = Process.GetProcessesByName(AppDefs.SpotifyProcessName);
                foreach (var p in freshProcs)
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    }

                    p.Dispose();
                }
            }

            if (_playNext)
            {
                Logger.Debug($"Trigger {nameof(_monitor.NextTrack)} when {nameof(_autoPlayAfterLaunch)} is {_autoPlayAfterLaunch} and {nameof(_playNext)} is {_playNext}.");

                _monitor.NextTrack();
            }
            else
            {
                _monitor.PlayPause();
            }

            _autoPlayAfterLaunch = false;
            _playNext = true;
        }

        _isSpotifyPlaying = !title.StartsWith(AppDefs.SpotifyProcessName) && !isSpotifyReady;
        var isAd = !title.Contains(" - ") && _isSpotifyPlaying;
        _currentTrackTitle = string.IsNullOrWhiteSpace(title) ? "No active media" :
            title == AppDefs.SpotifyNotRunning ? AppDefs.SpotifyNotRunning :
            isAd ? "Ad..." :
            _isSpotifyPlaying ? title : "Double-click to resume";
        _currentAdState = isAd;

        if (_trayIcon.ContextMenuStrip.InvokeRequired)
            _trayIcon.ContextMenuStrip.BeginInvoke(UpdateSpotifyStatus);
        else
            UpdateSpotifyStatus();

        if (!_isUserPaused)
        {
            if (isAd && IsAutoSkipAdViaRestartEnabled)
            {
                await ExecuteForceSkipAd();

                return;
            }

            await SpotifyAudioController.SetMute(isAd);

            if (_trayIcon.ContextMenuStrip.InvokeRequired)
                _trayIcon.ContextMenuStrip.BeginInvoke(() => UpdateTrayUi(isAd));
            else
                UpdateTrayUi(isAd);
        }
    }

    private void UpdateSpotifyStatus()
    {
        _trackItem.Text = $"{(_isSpotifyPlaying ? "🎵 Playing" : "⏸️ Paused")}: {_currentTrackTitle}";
        _playPauseItem.Text = _isSpotifyPlaying ? "⏸️ Pause Playback" : "▶️ Resume Playback";
    }

    private void LaunchSpotifyClient(bool autoPlayAfterLaunch, bool playNext = true)
    {
        var targetPath = string.Empty;

        try
        {
            var spotifyProcs = Process.GetProcessesByName(AppDefs.SpotifyProcessName);
            var isAlreadyRunning = spotifyProcs.Length > 0;

            foreach (var p in spotifyProcs) p.Dispose();

            if (!isAlreadyRunning)
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                           @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Spotify.exe"))
                {
                    if (key != null) targetPath = key.GetValue("")?.ToString() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                    using (var key = Registry.LocalMachine.OpenSubKey(
                               @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Spotify.exe"))
                    {
                        if (key != null) targetPath = key.GetValue("")?.ToString() ?? string.Empty;
                    }

                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    targetPath = Path.Combine(appData, @"Spotify\Spotify.exe");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = File.Exists(targetPath) ? targetPath : "spotify:",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    Arguments = "--autostart --minimized"
                });
            }

            _autoPlayAfterLaunch = autoPlayAfterLaunch;
            _playNext = playNext;
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "Spotify start failed 🚀", $"Error starting Spotify: {ex.Message}",
                ToolTipIcon.Warning);
        }
    }

    private void UpdateTrayUi(bool isMuted)
    {
        if (isMuted)
        {
            _trayIcon.Icon = _muteIcon;
            _launchItem.Visible = false;
            _playPauseItem.Visible = false;
            var text = "Spotify Muted 🤫\n🚫 Ad detected";
            _trayIcon.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text;
        }
        else
        {
            _trayIcon.Icon = _isSpotifyPlaying ? _playIcon : _pausedIcon;
            string text;
            if (_currentTrackTitle == AppDefs.SpotifyNotRunning)
            {
                _trayIcon.Icon = _notRunningIcon;
                _launchItem.Visible = true;
                _playPauseItem.Visible = false;
                text = $"SpotHusher {(_isUserPaused ? "🚫" : "🛡️")}\n⚠️ Spotify is not running";
            }
            else
            {
                _launchItem.Visible = false;
                _playPauseItem.Visible = true;
                text =
                    $"SpotHusher {(_isUserPaused ? "🚫" : "🛡️")}\n{(_isSpotifyPlaying ? "🎵 Playing" : "⏸️ Paused")}: {_currentTrackTitle}";
            }

            _trayIcon.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text;

            if (_isUserPaused)
            {
                _trayIcon.Icon = _disabledIcon;
            }
        }
    }
}