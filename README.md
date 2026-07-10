# SpotHusher 🤫

SpotHusher is a lightweight, zero-injection Windows system tray application designed to enhance your Spotify listening experience. By utilizing low-level Windows APIs and Core Audio COM interfaces, SpotHusher monitors Spotify's playback status in real-time to automatically mute advertisements or optionally skip them entirely by quickly cycling the client.

---

## ✨ Features

* **🚫 Smart Ad Muting:** Instantly mutes Spotify's specific audio session the millisecond an advertisement begins, and unmutes as soon as your music returns.
* **⚡ Force-Skip via Auto-Restart:** An optional high-speed skip feature that automatically restarts Spotify when an ad is detected, seamlessly advancing to the next track. <u>**Enabling this feature may cause screen flash.**</u>
* **🔊 Taskbar Volume Control:** Scroll your mouse wheel over the Windows taskbar to instantly adjust the master system volume smoothly.
* **🎧 Built-in Audio Output Switcher:** Easily change your active Windows playback device directly from the SpotHusher system tray menu.
* **⏸️ Smart Auto-Pause:** Automatically triggers a pause command when your Windows session locks or enters sleep/suspend modes, keeping your place in your playlist. <u>Double click icon in system tray to resume or pause again.</u>
* **📉 Global Memory Optimization:** Features a native system-wide RAM cleaner capable of flushing working sets, caches, and standby lists across the entire Windows OS, supporting multiple intensity modes (Safe, Aggressive, Emergency, Desperate). <u>**Enabling this feature requires administrator privileges and may cause the system to freeze temporarily.**</u>
* **🦆 Audio Ducking:** Automatically attenuates Spotify's volume based on background session states (e.g., during active voice communication or other audio activities) and seamlessly restores it. <u>**Enabling this feature may cause sudden volume changes.**</u>
* **📊 Monthly Listening Insights:** Local tracking mechanism that automatically logs playback history to generate local monthly insights, showcasing your top artist and most played track of the month.
* **🖱️ Mouse Macro Bindings:** Supports global mouse hotkey mapping, allowing you to bind specific mouse clicks or extra buttons (like Side Buttons) to custom macro commands or shortcuts.
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

![SpotHusher Screenshot1](https://raw.githubusercontent.com/shalahu/SpotHusher/refs/heads/master/Resources/screenshot1.jpg)

![SpotHusher Screenshot2](https://raw.githubusercontent.com/shalahu/SpotHusher/refs/heads/master/Resources/screenshot2.jpg)

![SpotHusher Screenshot3](https://raw.githubusercontent.com/shalahu/SpotHusher/refs/heads/master/Resources/screenshot3.jpg)

![SpotHusher Screenshot4](https://raw.githubusercontent.com/shalahu/SpotHusher/refs/heads/master/Resources/screenshot4.jpg)

### Configuration
Options can be managed instantly by right-clicking the SpotHusher icon in your system tray:
* **Auto-Skip Ads via Restart:** Toggles the process-restart skip method.
* **Auto-Launch Spotify With SpotHusher:** Automatically initializes Spotify alongside this tool.
* **Auto-Pause Spotify On Lock & Sleep:** Activates the session-state system event listeners.
* **Switch Audio Output:** Lists and toggles your current active playback hardware.
* **Scroll on Taskbar to Adjust System Volume:** Enactivates/disactivates global taskbar mouse wheel scrolling to control system volume.
* **System Memory Optimization (Multi-Mode):** Toggles and configures the global Windows RAM optimization tool with different intensity levels.
* **Audio Ducking Options (Multi-Option):** Enables background music suppression and allows adjusting ducking sensitivity, volume reduction percentage, and fade-back delay.
* **Export Monthly Listening Report:** Generates the report detailing your top artist and most played song of the month.
* **Mouse Macro Bindings:** Supports global mouse hotkey mapping via the `MouseMacroBindings` parameter in `appsettings.json`. This allows you to bind specific mouse clicks or extra buttons (like Side Buttons) to custom macro commands or media shortcuts.
  * **Config Key:** `"MouseMacroBindings"`
  * **Example String:** `"Middle:{MEDIANEXT}|-XButton1:{LEFT}|XButton2:{RIGHT}"`
  * **Syntax Rules:** 
    * Individual button mappings are separated by a pipe character (`|`).
    * The syntax relies on standard Windows SendKeys modifiers: `^` for **Ctrl**, `%` for **Alt**, and `+` for **Shift**.
    * **Disabling Rules:** Prepend a hyphen (`-`) before a button identifier (e.g., `-XButton1:...`) to temporarily disable that specific rule without deleting it.
  * **Key Mapping Reference:** To customize your macros, refer to the complete list of accepted key identifiers in the official [SendKeys Reference from Microsoft](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/sendkeys-statement).

Settings are saved locally in an automatically generated `appsettings.json` file inside the application directory.
## 🐛 Known Issues

* **Privilege Isolation (Admin vs. Standard User):** If your Spotify client is running with **Administrator privileges** while SpotHusher is running as a **Standard User**, certain core features will not function properly due to Windows User Interface Privilege Isolation (UIPI).
  * **Solution:** Ensure both applications are running under the same privilege level. It is highly recommended to run both as a standard user, or alternatively, launch SpotHusher as an Administrator.
---

## 📅 Changelog

### [V1.6]
* **Feature:** Added **System Memory Optimization** supporting multiple optimization levels to trim RAM usage across the entire Windows OS.
* **Feature:** Added **Audio Ducking** support, automatically lowering music volume when other communication apps are active.
* **Feature:** Added **Monthly Listening Report** export, allowing users to track their top artist and most played track each month.
* **Feature:** Added **Mouse Macro Bindings** supporting global mouse hotkey mapping (`MouseMacroBindings`) in `appsettings.json`, allowing users to bind custom shortcuts to mouse clicks (with toggle/disable support via the `-` prefix).

### [V1.5]
* **Feature:** Integrated global taskbar scrolling feature, allowing users to scroll the mouse wheel anywhere over the Windows taskbar to adjust the master system volume.

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

* **[LiteDB](https://github.com/litedb-org/LiteDB):** Serverless NoSQL embedded database for .NET, utilized for storing and managing local tracking history and playlist insights.

* **[globalmousekeyhook](https://github.com/gmamaladze/globalmousekeyhook):** Library to tap global keyboard and mouse hooks, used to process taskbar mouse wheel scrolling.

* **[NLog](https://github.com/nlog/NLog):** For robust, decoupled diagnostic logging under the hood. To avoid unnecessary bloat, it is decoupled via dynamic reflection; **if logging is required for troubleshooting, [NLog.dll](https://github.com/shalahu/SpotHusher/raw/refs/heads/master/Resources/NLog.dll) must reside in the app directory.**

---

## 💖 Acknowledgements & Inspiration
SpotHusher stands on the shoulders of several fantastic open-source projects and developer communities. We would like to express our gratitude and extend credits to:

* **[SpotifyAdMuter](https://github.com/enpandi/SpotifyAdMuter)** – For pioneering lightweight, non-intrusive approach to muting ads on Windows platforms.

* **[AudioSwitcher_v1](https://github.com/xenolightning/AudioSwitcher_v1)** – For providing a robust framework that greatly simplified the integration of C# with native Windows Core Audio APIs for endpoint switching.

* **[EZBlocker3](https://github.com/OpenByteDev/EZBlocker3)** – A major inspiration for the ad detection mechanism, showcasing how to cleanly utilize window titles and OS interaction for ad blocking without process injection.

* **[winMemoryOptimizer](https://github.com/sergiye/winMemoryOptimizer):** For providing key references on efficient Windows memory optimization techniques and working set management.

* **[Google's Gemini](https://gemini.google.com/)** – For assisting in structuring this project, crafting the documentation, and providing architectural refinement during development.

---

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.

## ⚠️ Disclaimer
This project is an independent open-source utility developed for educational and personal workflow automation purposes. It is not affiliated with, authorized, or endorsed by Spotify.
