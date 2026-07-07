using AudioSwitcher.AudioApi.CoreAudio;
using Gma.System.MouseKeyHook;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace SpotHusher
{
    public class TrayAppContext : ApplicationContext
    {
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
        private readonly ToolStripControlHost _playPauseItem;
        private readonly ToolStripMenuItem _shortcutItem;
        private readonly ToolStripMenuItem _volumeAdjustItem;
        private readonly ToolStripMenuItem _memoOptmizerItem;
        private readonly ToolStripLabel _trackItem;
        private readonly NotifyIcon _trayIcon;

        private bool _currentAdState;
        private string _currentTrackTitle;
        private List<CoreAudioDevice> _devices = [];
        private bool _isSpotifyPlaying;
        private bool _isUserPaused;
        private bool _wasHiddenBefore;
        private bool _autoPlayAfterLaunch;
        private bool _playNext;
        private Button _btnPlay;
        private Dictionary<MouseButtons, string> _mouseMacroBindings = new();
        private bool _isSuperuserMode;
        private bool _isAdmin;
        private System.Timers.Timer _delayTimer;

        private static IKeyboardMouseEvents? _globalHook;

        public TrayAppContext()
        {
            _isSuperuserMode = !string.IsNullOrEmpty(AppDefs.AppCfgs.MouseMacroBindings);
            _isAdmin = IsRunAsAdmin();

            _globalHook = Hook.GlobalEvents();
            _delayTimer = new System.Timers.Timer(3000) { AutoReset = false };

            _delayTimer.Elapsed += (o, args) =>
            {
                IconFactory.Clear();
                Task.Run(async () => await ProcessSpotifyTitle(_currentTrackTitle));
            };

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

            _trackItem = new ToolStripLabel($"🎵 Playing: {_currentTrackTitle}");

            _launchItem = new ToolStripMenuItem("🚀 Launch Spotify", null, (s, e) => { LaunchSpotifyClient(false); })
            { Visible = false };

            _playPauseItem = CreateMediaControlItem();

            _autoSkipAdItem = new ToolStripMenuItem("⚡ Auto-Skip Ads via Restart (May Cause Screen Flash)", null, (s, e) =>
            {
                AppDefs.AppCfgs.SetValue(nameof(AppCfgs.AutoSkipAdViaRestartEnabled), !AppDefs.AppCfgs.AutoSkipAdViaRestartEnabled);
                _autoSkipAdItem.Checked = AppDefs.AppCfgs.AutoSkipAdViaRestartEnabled;

                if (AppDefs.AppCfgs.AutoSkipAdViaRestartEnabled && _currentAdState && !_isUserPaused)
                {
                    Task.Run(async () => await ExecuteForceSkipAd());
                }
            });

            _autoLaunchItem = new ToolStripMenuItem(" ▶️ Auto-Launch Spotify with SpotHusher", null, (s, e) =>
            {
                AppDefs.AppCfgs.SetValue(nameof(AppCfgs.AutoLaunchSpotifyEnabled), !AppDefs.AppCfgs.AutoLaunchSpotifyEnabled);
                _autoLaunchItem.Checked = AppDefs.AppCfgs.AutoLaunchSpotifyEnabled;
            });

            _autoPauseItem = new ToolStripMenuItem("⏸️ Auto-Pause Spotify on Lock & Sleep", null, (s, e) =>
            {
                AppDefs.AppCfgs.SetValue(nameof(AppCfgs.AutoPausePlaybackEnabled), !AppDefs.AppCfgs.AutoPausePlaybackEnabled);
                _autoPauseItem.Checked = AppDefs.AppCfgs.AutoPausePlaybackEnabled;

                if (AppDefs.AppCfgs.AutoPausePlaybackEnabled)
                {
                    AddAutoPausePlaybackEvent();
                }
                else
                {
                    RemoveAutoPausePlaybackEvent();
                }
            });

            var audioDevicesMenu = new ToolStripMenuItem("🎧 Switch Audio Output");
            _volumeAdjustItem = new ToolStripMenuItem(string.Format(AppDefs.VolumeTextTemplate, "?", AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarPercentPerStep), null, (s, e) =>
            {
                AppDefs.AppCfgs.SetValue(nameof(AppCfgs.AdjustVolumeByScrollOnTaskbarEnabled), !AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarEnabled);
                _volumeAdjustItem.Checked = AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarEnabled;

                if (AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarEnabled)
                {
                    _globalHook.MouseWheelExt += OnMouseWheelExt;
                }
                else
                {
                    _globalHook.MouseWheelExt -= OnMouseWheelExt;
                }
            });

            _memoOptmizerItem = new ToolStripMenuItem(string.Format(AppDefs.MemoryTextTemplate, _isAdmin ? "Administrator" : "Standard User", GetMemoryLoad()))
            {
                DropDownItems = {
                    new ToolStripMenuItem("Safe", null, (s, ev) =>
                {
                    OptimizeMemory(MemoryAreas.Safe);
                }),
                    new ToolStripMenuItem("Aggressive", null, (s, ev) =>
                {
                    OptimizeMemory(MemoryAreas.Aggressive);
                }),
                    new ToolStripMenuItem("Emergency", null, (s, ev) =>
                {
                    OptimizeMemory(MemoryAreas.Emergency);
                }),
                    new ToolStripMenuItem("Desperate", null, (s, ev) =>
                    {
                        OptimizeMemory(MemoryAreas.Desperate);
                    })},
                Visible = _isSuperuserMode
            };

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

                    if (AppDefs.AppCfgs.AutoSkipAdViaRestartEnabled && _currentAdState)
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

                _globalHook.MouseWheelExt -= OnMouseWheelExt;
                _globalHook.MouseDownExt -= OnGlobalMouseDown;
                _delayTimer.Dispose();

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
                    _autoSkipAdItem.Checked = AppDefs.AppCfgs.AutoSkipAdViaRestartEnabled;
                    _autoLaunchItem.Checked = AppDefs.AppCfgs.AutoLaunchSpotifyEnabled;
                    _autoPauseItem.Checked = AppDefs.AppCfgs.AutoPausePlaybackEnabled;
                    _volumeAdjustItem.Checked = AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarEnabled;

                    _memoOptmizerItem.Text = string.Format(AppDefs.MemoryTextTemplate, _isAdmin ? "Administrator" : "Standard User", GetMemoryLoad());

                    if (_currentTrackTitle != AppDefs.SpotifyNotRunning) UpdateSpotifyStatus();

                    if (audioDevicesMenu.DropDownItems.Count == 0)
                        switch (_devices)
                        {
                            case null:
                                return;

                            case { Count: 0 }:
                                {
                                    var item = new ToolStripMenuItem("No audio devices found. Try again later...") { Enabled = false };
                                    audioDevicesMenu.DropDownItems.Add(item);

                                    return;
                                }
                        }

                    if (_devices.Count > audioDevicesMenu.DropDownItems.Count)
                    {
                        audioDevicesMenu.DropDownItems.Clear();
                    }

                    foreach (var device in _devices)
                    {
                        var deviceItem = new ToolStripMenuItem(device.FullName);

                        if (device.IsDefaultDevice)
                        {
                            _volumeAdjustItem.Text = string.Format(AppDefs.VolumeTextTemplate, device.Volume, AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarPercentPerStep);
                            deviceItem.Enabled = false;
                            deviceItem.Checked = true;
                            deviceItem.Font = new Font(deviceItem.Font, FontStyle.Bold);
                        }

                        deviceItem.Image = LoadIconFromResourceString(device.IconPath);

                        deviceItem.Click += async (s, ev) =>
                        {
                            foreach (ToolStripMenuItem item in audioDevicesMenu.DropDownItems)
                            {
                                item.Enabled = true;
                                item.Checked = false;
                                item.Font = new Font(deviceItem.Font, FontStyle.Regular);
                            }

                            deviceItem.Enabled = false;
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
            contextMenu.Items.Add(_volumeAdjustItem);
            contextMenu.Items.Add(_memoOptmizerItem);
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

            if (AppDefs.AppCfgs.AutoLaunchSpotifyEnabled)
            {
                LaunchSpotifyClient(false);
            }

            if (AppDefs.AppCfgs.AutoPausePlaybackEnabled)
            {
                AddAutoPausePlaybackEvent();
            }

            if (AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarEnabled)
            {
                _globalHook.MouseWheelExt += OnMouseWheelExt;
            }

            if (_isSuperuserMode)
            {
                _mouseMacroBindings = LoadMouseMacroBindingsFromString(AppDefs.AppCfgs.MouseMacroBindings);

                _globalHook.MouseDownExt += OnGlobalMouseDown;
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

        private static uint GetMemoryLoad()
        {
            return MemoryOptimizer.GetMemoryStatus().MemoLoad;
        }

        private static Dictionary<MouseButtons, string> LoadMouseMacroBindingsFromString(string value)
        {
            Dictionary<MouseButtons, string> _bindings = new();

            if (string.IsNullOrWhiteSpace(value)) return _bindings;

            var pairs = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split(':', 2);
                if (parts.Length == 2 && Enum.TryParse(parts[0], out MouseButtons button))
                {
                    _bindings[button] = parts[1];
                }
            }

            return _bindings;
        }

        private void OnGlobalMouseDown(object sender, MouseEventExtArgs e)
        {
            if (_mouseMacroBindings.TryGetValue(e.Button, out string sendKeysPattern))
            {
                if (!string.IsNullOrWhiteSpace(sendKeysPattern))
                {
                    SendKeys.SendWait(sendKeysPattern);
                    e.Handled = true;
                }
            }
        }

        private ToolStripControlHost CreateMediaControlItem()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Size = new Size(256, 52),
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                WrapContents = false
            };

            Button btnPrev = CreateMediaButton("⏮", (s, e) => { _monitor.PreviousTrack(); });
            _btnPlay = CreateMediaButton("⏸️ / ▶️", (s, e) => { PlayPause(); });
            Button btnNext = CreateMediaButton("⏭", (s, e) => { _monitor.NextTrack(); });

            panel.Controls.Add(btnPrev);
            panel.Controls.Add(_btnPlay);
            panel.Controls.Add(btnNext);

            ToolStripControlHost hostItem = new ToolStripControlHost(panel);

            hostItem.AutoSize = false;
            hostItem.Size = panel.Size;
            hostItem.Margin = Padding.Empty;
            hostItem.Padding = Padding.Empty;

            return hostItem;
        }

        private static Button CreateMediaButton(string text, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(44, 44),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4, 4, 4, 4)
            };

            btn.Click += onClick;

            return btn;
        }

        private void OnMouseWheelExt(object? sender, MouseEventExtArgs e)
        {

            try
            {
                IntPtr hWndUnderMouse = WindowFromPoint(new Point { x = e.X, y = e.Y });
                if (IsTaskbarWindow(hWndUnderMouse))
                {
                    _delayTimer.Stop();

                    bool isScrollUp = e.Delta > 0;

                    var volume = SpotifyAudioController.AdjustVolume(isScrollUp);

                    IconFactory.UpdateIcon((int)volume, _trayIcon);

                    _delayTimer.Start();

                    e.Handled = true;
                }
            }
            catch
            {
                // ignored
            }
        }

        private void PlayPause()
        {
            if (!_currentAdState)
            {
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
        }

        private void OptimizeMemory(MemoryAreas areas)
        {
            if (!_isAdmin)
            {
                DialogResult result = MessageBox.Show(
                    "This memory optimizer requires administrator privileges to access advanced cleaning features.\n\nWould you like to restart the application as an administrator?",
                    "Privilege Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.Yes)
                {
                    RestartAsAdmin();
                }

                return;
            }

            _delayTimer.Stop();

            string shortReport = MemoryOptimizer.Optimize(areas, _trayIcon);
            
            IconFactory.UpdateIcon((int)GetMemoryLoad(), _trayIcon);

            Logger.Debug(shortReport);

            _trayIcon.ShowBalloonTip(3000, "Memory optimized 🧹", shortReport,
                ToolTipIcon.Info);

            _delayTimer.Start();
        }

        private static bool IsRunAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static void RestartAsAdmin()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                Arguments = $"--wait-for-pid {Process.GetCurrentProcess().Id}",
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception)
            {
                MessageBox.Show("Elevation was denied. Advanced memory cleaning features will be unavailable.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static bool IsTaskbarWindow(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero) return false;
                var buffer = new StringBuilder(256);
                int length = GetClassNameW(hWnd, buffer, buffer.Capacity);
                if (length == 0) return false;
                string className = buffer.ToString();

                var targets = new List<string>() { "TrayShowDesktopButtonWClass", "TrayClockWClass", "MSTaskListWClass", "ToolbarWindow32", "Shell_TrayWnd", "SIBTrayButton", "MSTaskSwWClass" };

                return targets.Any(i => string.Compare(i, className, StringComparison.OrdinalIgnoreCase) == 0);
            }
            catch
            {
                // ignored
            }

            return false;
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

                        // not suitable for awaitable method of WaitForExit, we MUST use a synchronous call here
                        p.WaitForExit();
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
                    await Task.Delay(TimeSpan.FromMilliseconds(1000));

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

            _isSpotifyPlaying = !title.StartsWith(AppDefs.SpotifyProcessName) && title != AppDefs.SpotifyPausedMessage && !isSpotifyReady;
            var isAd = !title.Contains(" - ") && _isSpotifyPlaying;
            _currentTrackTitle = string.IsNullOrWhiteSpace(title) ? "No active media" :
                title == AppDefs.SpotifyNotRunning ? AppDefs.SpotifyNotRunning :
                isAd ? "Ad..." :
                _isSpotifyPlaying ? title : AppDefs.SpotifyPausedMessage;
            _currentAdState = isAd;

            if (_trayIcon.ContextMenuStrip.InvokeRequired)
                _trayIcon.ContextMenuStrip.BeginInvoke(UpdateSpotifyStatus);
            else
                UpdateSpotifyStatus();

            if (!_isUserPaused)
            {
                if (isAd && AppDefs.AppCfgs.AutoSkipAdViaRestartEnabled)
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
            _btnPlay.Text = _isSpotifyPlaying ? "⏸️" : "▶️";
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

        public static Image? LoadIconFromResourceString(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;

            try
            {
                string expandedPath = Environment.ExpandEnvironmentVariables(resourcePath);
                int commaIndex = expandedPath.LastIndexOf(',');

                if (commaIndex == -1) return null;

                string dllPath = expandedPath[..commaIndex];
                string idStr = expandedPath[(commaIndex + 1)..];

                if (!int.TryParse(idStr, out int resourceId)) return null;

                int targetId = Math.Abs(resourceId);
                IntPtr[] phIcon = new IntPtr[1];
                uint[] pIconId = new uint[1];
                uint result = PrivateExtractIconsW(dllPath, resourceId, 16, 16, phIcon, pIconId, 1, 0);

                if (result == 0 || phIcon[0] == IntPtr.Zero) return null;

                IntPtr hIcon = phIcon[0];
                using Icon icon = Icon.FromHandle(hIcon);
                Image bitmap = icon.ToBitmap();
                DestroyIcon(hIcon);

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint PrivateExtractIconsW(string szFileName, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr hIcon);


        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int x; public int y; }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}
