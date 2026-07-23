# 🎧 PcAudioStreamer - Real-Time Low-Latency PC to Android Audio Streamer

**PcAudioStreamer** is a ultra-low-latency, zero-lag PC audio streaming system that captures 48kHz High-Quality Stereo audio from Windows (WASAPI Loopback) and streams it directly to an Android phone over **USB Tethering**.

It includes a global hotkey toggler (`Ctrl + End`) to seamlessly switch between **Phone Only** (mutes PC speakers and routes 100% audio to phone headphones) and **PC Speakers Only**.

---

## ✨ Features

- **⚡ Ultra-Low Latency (<10ms):** Direct WASAPI loopback capture and raw WebSocket streaming over direct USB Ethernet (`10.39.227.158`).
- **🎧 High Quality 48kHz Stereo:** Clean 1:1 float32-to-PCM16 conversion delivering studio/CD quality sound without static or noise.
- **⌨️ Global Hotkey Toggle (`Ctrl + End`):** Toggle between **Phone Headphones** and **PC Speakers** instantly.
- **🔒 USB Tethering Locked:** Works cleanly over Android USB Tethering without forcing USB Audio DAC mode.
- **🛡️ Auto-Mute Persistence:** Auto-suppresses Windows speaker auto-unmuting when master volume is adjusted in Phone mode.
- **⚡ Startup Support:** Optional "Run on Windows Startup" context menu toggle.

---

## 🛠️ Project Architecture

```
pc-audio-streamer/
├── win_app/                # C# Windows Tray Application
│   ├── Program.cs          # Main WASAPI loopback capture & TCP WebSocket broadcaster
│   ├── NAudio.dll          # NAudio core assembly
│   ├── NAudio.Wasapi.dll   # NAudio WASAPI driver assembly
│   └── NAudio.Core.dll     # NAudio audio abstractions
└── android_app/            # Android Receiver Native Service App
    ├── AndroidManifest.xml # Android manifest with Foreground Service permissions
    └── src/com/audiostreamer/
        ├── MainActivity.java        # Receiver UI & service controller
        ├── AudioStreamService.java  # AudioTrack receiver & low-latency player
        └── HttpWebSocketClient.java # Zero-lag WebSocket binary frame parser
```

---

## 🚀 How to Build & Run

### 1. Windows Application (`win_app`)
- Requirements: Windows 10/11, .NET Framework / Roslyn C# compiler.
- Compile using Roslyn C# compiler:
  ```powershell
  csc /target:exe /out:PcAudioStreamer.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll,netstandard.dll,NAudio.dll,NAudio.Wasapi.dll,NAudio.Core.dll Program.cs
  ```
- Run `PcAudioStreamer.exe`.

### 2. Android Receiver App (`android_app`)
- Requirements: Android SDK 24+ (Android 7.0+).
- Compile APK with `javac`, `d8`, `aapt2`, `apksigner`:
- Install on Android phone:
  ```bash
  adb install -r AudioReceiver.apk
  ```
- Enable **USB Tethering** on your Android phone and launch the app!

---

## 🎹 Controls
- **`Ctrl + End`**: Toggle output between **Phone Headphones** and **PC Speakers**.
- **System Tray Icon**: Right-click the system tray icon to view active status, toggle modes, or enable **Run on Windows Startup**.

---

## 📄 License
MIT License
