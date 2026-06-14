# ScreenShield
[Russian Readme](https://github.com/DOGi23/Screen_Shield/blob/main/README.ru.md)

ScreenShield is a lightweight, high-performance Windows utility designed to prevent desktop elements, specified application windows, and taskbar items from being captured by screen-sharing, recording, and streaming software (e.g., OBS, Discord, Zoom, etc.).

---

## 🌟 Key Features

- **🔒 Real-time Window Exclusion**: Shields selected applications by setting their display affinity, hiding them from capturing software while keeping them fully visible to you.
- **🖼️ Desktop & Wallpaper Guard**: Prevents background wallpaper and selected desktop shortcuts/icons from appearing on stream.
- **⌨️ Global Hotkeys**: Toggle protection on the fly via `Ctrl + Shift + S`.
- **🔊 Auditory Cues**: Provides instant stereo sound effects upon activation or deactivation of the protection, allowing you to know the state of protection while playing games or streaming in full-screen.
- **🔔 Notifications**: Beautiful, non-intrusive notification toasts.
- **📂 Drag & Drop Wallpapers**: Easily drag and drop images directly into the panel to set your stream-safe background.
- **🚀 Run at Startup**: Optional start-on-boot configuration to launch minimized directly to the Windows system tray.
- **🔗 Taskbar Hiding (Optional)**: Hides protected app buttons from the Windows Taskbar (lower bar) for complete privacy.

---

## 🛠️ Technology Stack

- **Framework**: .NET 8.0 / WPF (Windows Presentation Foundation)
- **Language**: C# 12
- **Interoperability**: Windows Native APIs (User32, Shell32 via P/Invoke, COM Interfaces for `ITaskbarList`)
- **System Querying**: `System.Management` for WMI queries

---

## 🚀 How to Build & Run

### Prerequisites
- Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer.
- Visual Studio 2022 (with .NET Desktop Development workload) or VS Code.

### Building via CLI
Open your command prompt or PowerShell in the root directory and run:
```bash
# Restore dependencies and build in Release mode
dotnet build -c Release
```

The compiled binaries will be output to:
`bin/Release/net8.0-windows/ScreenShield.exe`

---

## 📦 Project Structure

- `MainWindow.xaml` / `MainWindow.xaml.cs`: Core UI, event handling, and configuration bindings.
- `DesktopManager.cs`: Controls the wallpaper and shortcut exclusion overlay window.
- `ProcessShieldManager.cs`: Manages processes monitored for automatic window capture exclusion.
- `AppConfig.cs`: Configuration management (loading/saving JSON config).
- `AntiDebug.cs`: Optional security check to detect debugging tools.
- `NativeMethods.cs`: P/Invoke signatures and Win32 API bindings.

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**.

You are free to use, modify, and distribute this software, but any derivative works (forks) **must** remain open-source and be distributed under the same GPLv3 license. Commercializing closed-source forks is prohibited.
