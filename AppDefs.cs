namespace SpotHusher
{
    public static class AppDefs
    {
        public const string GuidRegex =
            @"([a-fA-F0-9]{8}[-][a-fA-F0-9]{4}[-][a-fA-F0-9]{4}[-][a-fA-F0-9]{4}[-][a-fA-F0-9]{12})";

        public const string SpotifyProcessName = "Spotify";

        public const string SpotifyNotRunning = "Spotify is not running";

        public const string SpotifyPrimaryGuiWindowsClassNamePrefix = "Chrome_WidgetWin_";

        public const string SpotifyIsReadyWindowsClassNamePrefix = "Chrome";

        public const string VolumeTextTemplate = "🔊 Scroll on Taskbar to Adjust System Volume (Current: {0}%, Step: {1}%)";

        public static AppCfgs AppCfgs = AutoSaveConfig.LoadFromFile<AppCfgs>();
    }
}
