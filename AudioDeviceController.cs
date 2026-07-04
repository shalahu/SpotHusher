using System.Diagnostics;
using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;

namespace SpotHusher;

public class AudioDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public static class AudioDeviceController
{
    private static readonly CoreAudioController CoreAudioController = new();

    private static List<CoreAudioDevice>? _activeDevices;

    public static async Task<bool> SetDefaultPlaybackDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        try
        {
            _activeDevices ??= await GetActiveDevices();

            var device = _activeDevices
                .FirstOrDefault(d => d.Id.ToString() == deviceId);

            if (device != null) return CoreAudioController.SetDefaultDevice(device);

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioSwitcher Error]: {ex.Message}");
            return false;
        }
    }

    public static async Task<List<AudioDevice>> GetPlaybackDevices()
    {
        _activeDevices ??= await GetActiveDevices();

        return _activeDevices.Select(i => new AudioDevice
            { Id = i.Id.ToString(), IsDefault = i.IsDefaultDevice, Name = i.FullName }).ToList();
    }

    private static async Task<List<CoreAudioDevice>> GetActiveDevices()
    {
        return (await CoreAudioController.GetPlaybackDevicesAsync(DeviceState.Active)).ToList();
    }
}