using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Observables;
using AudioSwitcher.AudioApi.Session;
using System.Collections.Concurrent;

namespace SpotHusher
{
    public class SpotifyAudioController
    {
        private static readonly CoreAudioController CoreAudioController = new();

        private static List<CoreAudioDevice>? _activeCoreAudioDevices;

        private static readonly ConcurrentDictionary<string, IDisposable> ChangedSubs = new();

        public static Action OnSessonStateChanged { get; set; }

        public static async Task SetMute(bool? mute = null)
        {
            var subscribeStateChanged = AppDefs.AppCfgs.DuckingAttenuationPercent != 0;
            if (!subscribeStateChanged)
            {
                DisposeSub();
                ChangedSubs.Clear();
            }

            _activeCoreAudioDevices ??= await GetActiveDevices();

            try
            {
                for (int i = 0; i < _activeCoreAudioDevices.Count; i++)
                {
                    var device = _activeCoreAudioDevices[i];

                    var audioSessionController = device.SessionController;

                    if (subscribeStateChanged && !ChangedSubs.ContainsKey(device.Id.ToString()))
                    {
                        var controllerCreatedSub = audioSessionController.SessionCreated.Subscribe(session =>
                        {
                            if (session.DisplayName != AppDefs.SpotifyProcessName && !ChangedSubs.ContainsKey(session.Id))
                            {
                                SubscribeStateChanged(session);
                            }
                        });

                        if (!ChangedSubs.TryAdd(device.Id.ToString(), controllerCreatedSub))
                        {
                            controllerCreatedSub.Dispose();
                        }
                    }

                    var allSessions = await audioSessionController.AllAsync();

                    foreach (var session in allSessions)
                    {
                        if (session.DisplayName == AppDefs.SpotifyProcessName)
                        {
                            //session.IsMuted = mute;

                            var targetVolume = !mute.HasValue ? 100 - AppDefs.AppCfgs.DuckingAttenuationPercent : mute.Value ? 0 : 100;

                            targetVolume = Math.Clamp(targetVolume, 0, 100);
                            int step = session.Volume < targetVolume ? 5 : -5;

                            while (session.Volume != targetVolume)
                            {
                                if (Math.Abs(targetVolume - session.Volume) < Math.Abs(step))
                                {
                                    session.Volume = targetVolume;
                                }
                                else
                                {
                                    session.Volume += step;
                                }

                                await Task.Delay(25);
                            }
                        }
                        else
                        {
                            if (subscribeStateChanged && !ChangedSubs.ContainsKey(session.Id))
                            {
                                SubscribeStateChanged(session);
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return;

            void SubscribeStateChanged(IAudioSession session)
            {
                var sub = session.StateChanged.Subscribe(args =>
                {
                    Task.Run(async () =>
                    {
                        if (OnSessonStateChanged != null)
                        {
                            OnSessonStateChanged();
                        }
                        else
                        {
                            await SetMute(args.State == AudioSessionState.Active ? null : false);
                        }

                        if (args.State == AudioSessionState.Expired && ChangedSubs.TryRemove(args.Session.Id, out var disSub))
                        {
                            disSub?.Dispose();
                        }
                    });
                });

                if (!ChangedSubs.TryAdd(session.Id, sub))
                {
                    sub.Dispose();
                }
            }
        }

        public static double AdjustVolume(bool isVolumeUp)
        {
            try
            {
                var defaultDevice = _activeCoreAudioDevices?.First(i => i.IsDefaultDevice);

                if (defaultDevice != null)
                {
                    if (isVolumeUp)
                    {
                        defaultDevice.Volume += AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarPercentPerStep;
                    }
                    else
                    {
                        defaultDevice.Volume -= AppDefs.AppCfgs.AdjustVolumeByScrollOnTaskbarPercentPerStep;
                    }

                    return defaultDevice.Volume;
                }
            }
            catch
            {
                // ignored
            }

            return 0;
        }

        public static async Task<bool> SetDefaultPlaybackDevice(Guid deviceId)
        {
            try
            {
                _activeCoreAudioDevices ??= await GetActiveDevices();

                var device = _activeCoreAudioDevices
                    .FirstOrDefault(d => d.Id == deviceId);

                if (device != null)
                {
                    return await device.SetAsDefaultAsync();
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<List<CoreAudioDevice>> GetActiveDevices()
        {
            try
            {
                _activeCoreAudioDevices ??= (await CoreAudioController.GetDevicesAsync(DeviceState.Active)).ToList();

                return _activeCoreAudioDevices;
            }
            catch
            {
                // ignored
            }

            return [];
        }

        public static void Dispose()
        {
            CoreAudioController.Dispose();

            DisposeSub();
        }

        private static void DisposeSub()
        {
            foreach (var sub in ChangedSubs.Values)
            {
                sub?.Dispose();
            }
        }
    }
}