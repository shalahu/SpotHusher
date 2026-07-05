# SpotHusher 🤫

SpotHusher is a lightweight, zero-injection Windows system tray application designed to enhance your Spotify listening experience. By utilizing low-level Windows APIs and Core Audio COM interfaces, SpotHusher monitors Spotify's playback status in real-time to automatically mute advertisements or optionally skip them entirely by quickly cycling the client.

---

## ✨ Features

* **🚫 Smart Ad Muting:** Instantly mutes Spotify's specific audio session the millisecond an advertisement begins, and unmutes as soon as your music returns.
* **⚡ Force-Skip via Auto-Restart:** An optional high-speed skip feature that automatically restarts Spotify when an ad is detected, seamlessly advancing to the next track. <u>**Enable this feature may cause screen flash.**</u>
* **🎧 Built-in Audio Output Switcher:** Easily change your active Windows playback device directly from the SpotHusher system tray menu.
* **⏸️ Smart Auto-Pause:** Automatically triggers a pause command when your Windows session locks or enters sleep/suspend modes, keeping your place in your playlist. <u>Double click icon in system tray to resume or pause again.</u>
* **🚀 Seamless Automation:** Includes options to automatically launch Spotify when SpotHusher starts, run at Windows startup, and quickly generate a desktop shortcut.

---

## 🛠️ How It Works

SpotHusher achieves its functionality through clean native Windows integrations:

1.  **Playback Monitoring:** It establishes a `SetWinEventHook` native hook to listen for window title changes across the system. It safely extracts track details and isolates advertisement signatures from the Spotify process structure without deep process hooking.
2.  **Audio Session Targeting:** Using the Windows Core Audio APIs (`IMMDeviceEnumerator`, `IAudioSessionManager2`), it locates the exact audio sub-session corresponding to Spotify's PID and applies an explicit mute state via `ISimpleAudioVolume`. This ensures your master volume and other applications remain completely unaffected.

---

## 📋 Requirements

* **OS:** Windows 10 / Windows 11
* **Runtime:** [.NET 10.0 Runtime](https://dotnet.microsoft.com/download) (or higher)
* **Target Application:** Spotify Desktop Client (Windows Store or standalone installation)

---

## 🚀 Getting Started

### Installation
1. Download the latest release from the [Releases](https://github.com/shalahu/SpotHusher/releases) tab.
2. Extract the files to a local directory of your choice.
3. Run `SpotHusher.exe`.

### Configuration & Preview
Options can be managed instantly by right-clicking the SpotHusher icon in your system tray:

![SpotHusher Screenshot](https://raw.githubusercontent.com/shalahu/SpotHusher/refs/heads/master/Resources/screenshot.jpg)

### Configuration
Options can be managed instantly by right-clicking the SpotHusher icon in your system tray:
* **Auto-Skip Ads via Restart:** Toggles the process-restart skip method.
* **Auto-Launch Spotify With SpotHusher:** Automatically initializes Spotify alongside this tool.
* **Auto-Pause Spotify On Lock & Sleep:** Activates the session-state system event listeners.
* **Switch Audio Output:** Lists and toggles your current active playback hardware.

Settings are saved locally in an automatically generated `appsettings.json` file inside the application directory.
## 🐛 Known Issues

* **Privilege Isolation (Admin vs. Standard User):** If your Spotify client is running with **Administrator privileges** while SpotHusher is running as a **Standard User**, certain core features will not function properly due to Windows User Interface Privilege Isolation (UIPI).
  * **Solution:** Ensure both applications are running under the same privilege level. It is highly recommended to run both as a standard user, or alternatively, launch SpotHusher as an Administrator.
---

## 📅 Changelog

### [V1.4]
* **Feature:** Added smart auto-pause behavior when the Windows session locks or enters suspend/sleep states.

### [V1.3]
* **Feature:** Added option to automatically launch the Spotify client whenever SpotHusher starts.

### [V1.2]
* **Feature:** Integrated a native Windows Core Audio switcher to change audio output devices on the fly.

### [V1.1]
* **Feature:** Introduced high-speed "Force-Skip Ads via Restart" method to cycle the client instead of just muting.

### [V1.0] - Initial Release
* **Core Function:** Real-time Spotify advertisement muting.
* **Automation:** Added options to run at Windows startup and generate desktop shortcuts easily.
* **Control:** Added toggle to temporarily disable/enable SpotHusher protection from the tray menu.

---

## 🛠️ Development & Compilation

To clone and build SpotHusher from source:

```bash
# Clone the repository
git clone https://github.com/shalahu/SpotHusher.git

# Navigate to the project directory
cd SpotHusher

# Build the project
dotnet build -c Release
```

### Key Dependencies
* **[AudioSwitcher.AudioApi.CoreAudio](https://github.com/xenolightning/AudioSwitcher):** For managing and switching native Windows audio playback endpoints.

* **[NLog](https://github.com/nlog/NLog):** For robust, decoupled diagnostic logging under the hood. To avoid unnecessary bloat, it is decoupled via dynamic reflection; **if logging is required for troubleshooting, [NLog.dll](https://github.com/shalahu/SpotHusher/raw/refs/heads/master/Resources/NLog.dll) must reside in the app directory.**

---

## 💖 Acknowledgements & Inspiration
SpotHusher stands on the shoulders of several fantastic open-source projects and developer communities. We would like to express our gratitude and extend credits to:

* **[SpotifyAdMuter](https://github.com/enpandi/SpotifyAdMuter)** – For pioneering lightweight, non-intrusive approach to muting ads on Windows platforms.

* **[AudioSwitcher_v1](https://github.com/xenolightning/AudioSwitcher_v1)** – For providing a robust framework that greatly simplified the integration of C# with native Windows Core Audio APIs for endpoint switching.

* **[EZBlocker3](https://github.com/OpenByteDev/EZBlocker3)** – A major inspiration for the ad detection mechanism, showcasing how to cleanly utilize window titles and OS interaction for ad blocking without process injection.

* **[Google's Gemini](https://gemini.google.com/)** – For assisting in structuring this project, crafting the documentation, and providing architectural refinement during development.

---

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.

## ⚠️ Disclaimer
This project is an independent open-source utility developed for educational and personal workflow automation purposes. It is not affiliated with, authorized, or endorsed by Spotify.
