using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace SpotHusher
{
    public class SpotifyAudioController
    {
        private static readonly CoreAudioController CoreAudioController = new();

        private static List<CoreAudioDevice>? _activeCoreAudioDevices;

        public static async Task SetMute(bool mute)
        {
            _activeCoreAudioDevices ??= await GetActiveDevices();

            try
            {
                for (int i = 0; i < _activeCoreAudioDevices.Count; i++)
                {
                    var device = _activeCoreAudioDevices[i];

                    var audioSessionController = device.SessionController;
                    var activeSessions = await audioSessionController.AllAsync();

                    foreach (var activeSession in activeSessions)
                    {
                        if (activeSession.DisplayName == AppDefs.SpotifyProcessName)
                        {
                            activeSession.IsMuted = mute;
                        }
                    }
                }

            }
            catch
            {
                // ignored
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

                    Logger.Debug($"{defaultDevice.FullName} has new valume {defaultDevice.Volume}.");

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
        }
    }
}