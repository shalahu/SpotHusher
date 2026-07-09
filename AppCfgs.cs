using System.Text.Json;

namespace SpotHusher
{
    public abstract class AutoSaveConfig
    {
        protected virtual string FileName => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        public static T LoadFromFile<T>() where T : AutoSaveConfig, new()
        {
            T configInstance = new T();

            string targetFile = configInstance.FileName;

            if (!File.Exists(targetFile))
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                byte[] initialBytes = JsonSerializer.SerializeToUtf8Bytes(configInstance, typeof(T), options);
                File.WriteAllBytes(targetFile, initialBytes);

                return configInstance;
            }

            try
            {
                byte[] jsonBytes = File.ReadAllBytes(targetFile);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                T loadedInstance = JsonSerializer.Deserialize<T>(jsonBytes, options);

                return loadedInstance ?? configInstance;
            }
            catch
            {
                return configInstance;
            }
        }

        public void SetValue<T>(string propertyName, T newValue)
        {
            var property = GetType().GetProperty(propertyName);
            if (property == null) throw new ArgumentException(propertyName);

            property.SetValue(this, newValue);

            var options = new JsonSerializerOptions { WriteIndented = true };
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(this, this.GetType(), options);
            File.WriteAllBytes(FileName, jsonBytes);
        }
    }

    public class AppCfgs : AutoSaveConfig
    {
        public bool AutoSkipAdViaRestartEnabled { get; set; }

        public bool AutoLaunchSpotifyEnabled { get; set; }

        public bool AutoPausePlaybackEnabled { get; set; }

        public bool AdjustVolumeByScrollOnTaskbarEnabled { get; set; }

        public int AdjustVolumeByScrollOnTaskbarPercentPerStep { get; set; } = 1;

        public string MouseMacroBindings { get; set; } = string.Empty;

        public int DuckingAttenuationPercent { get; set; } = 0;
    }
}
