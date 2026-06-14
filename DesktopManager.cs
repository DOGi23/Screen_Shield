using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ScreenShield.Security
{
    public class DesktopManager
    {
        // --- Native P/Invokes for Monitor Enumeration ---
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        private string _originalWallpaperPath = "";
        private bool _isShieldActive = false;
        private readonly List<DesktopOverlayWindow> _overlayWindows = new List<DesktopOverlayWindow>();
        private string _cleanWallpaperPath = "";
        private bool _weHidDesktopIcons = false;
        public bool IsShieldActive => _isShieldActive;

        private bool _isWallpaperShieldEnabled = false;
        private bool _isIconsShieldEnabled = false;

        public bool IsWallpaperShieldEnabled => _isWallpaperShieldEnabled;
        public bool IsIconsShieldEnabled => _isIconsShieldEnabled;

        public void ApplyShieldState(bool enableWallpaper, bool enableIcons, string customCaptureWallpaper, List<string> hiddenIconNames)
        {
            try
            {
                _isWallpaperShieldEnabled = enableWallpaper;
                _isIconsShieldEnabled = enableIcons;

                bool shouldShowOverlay = enableWallpaper || enableIcons;

                // 1. Manage native desktop icons visibility
                if (enableIcons)
                {
                    if (AreDesktopIconsVisible())
                    {
                        ToggleDesktopIcons();
                        _weHidDesktopIcons = true;
                    }
                }
                else
                {
                    if (_weHidDesktopIcons || !AreDesktopIconsVisible())
                    {
                        ToggleDesktopIcons();
                        _weHidDesktopIcons = false;
                    }
                }

                // 2. Manage capture wallpaper in the OS
                if (enableWallpaper)
                {
                    if (string.IsNullOrEmpty(_originalWallpaperPath))
                    {
                        _originalWallpaperPath = GetCurrentWallpaperPath(customCaptureWallpaper);
                    }

                    string wallpaperToSet = "";
                    string publicDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "ScreenShield");
                    try
                    {
                        if (!Directory.Exists(publicDir))
                        {
                            Directory.CreateDirectory(publicDir);
                        }
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(customCaptureWallpaper) && File.Exists(customCaptureWallpaper))
                    {
                        try
                        {
                            string tempCustomBg = Path.Combine(publicDir, "screenshield_custom_bg.png");
                            
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.UriSource = new Uri(customCaptureWallpaper);
                            image.EndInit();

                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(image));
                            using (var stream = new FileStream(tempCustomBg, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                encoder.Save(stream);
                            }

                            wallpaperToSet = tempCustomBg;
                        }
                        catch
                        {
                            try
                            {
                                string tempCustomBg = Path.Combine(publicDir, "screenshield_custom_bg" + Path.GetExtension(customCaptureWallpaper));
                                File.Copy(customCaptureWallpaper, tempCustomBg, true);
                                wallpaperToSet = tempCustomBg;
                            }
                            catch
                            {
                                wallpaperToSet = customCaptureWallpaper;
                            }
                        }
                    }
                    else
                    {
                        _cleanWallpaperPath = Path.Combine(publicDir, "screenshield_clean_bg.png");
                        CreateSolidColorPng(_cleanWallpaperPath, System.Windows.Media.Color.FromRgb(30, 30, 36)); // Sleek neutral dark gray
                        wallpaperToSet = _cleanWallpaperPath;
                    }

                    NativeMethods.SystemParametersInfo(
                        NativeMethods.SPI_SETDESKTOPWALLPAPER, 
                        0, 
                        wallpaperToSet, 
                        NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE
                    );
                }
                else
                {
                    bool wasOriginalPathEmpty = string.IsNullOrEmpty(_originalWallpaperPath);
                    if (wasOriginalPathEmpty)
                    {
                        _originalWallpaperPath = GetCurrentWallpaperPath(customCaptureWallpaper);
                    }

                    if (!string.IsNullOrEmpty(_originalWallpaperPath) && File.Exists(_originalWallpaperPath))
                    {
                        if (!wasOriginalPathEmpty)
                        {
                            NativeMethods.SystemParametersInfo(
                                NativeMethods.SPI_SETDESKTOPWALLPAPER, 
                                0, 
                                _originalWallpaperPath, 
                                NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE
                            );
                        }
                        else
                        {
                            string currentOSWallpaper = GetCurrentRegistryWallpaper();
                            if (IsCaptureWallpaper(currentOSWallpaper, customCaptureWallpaper) || string.IsNullOrEmpty(currentOSWallpaper))
                            {
                                NativeMethods.SystemParametersInfo(
                                    NativeMethods.SPI_SETDESKTOPWALLPAPER, 
                                    0, 
                                    _originalWallpaperPath, 
                                    NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE
                                );
                            }
                        }
                        _originalWallpaperPath = "";
                    }

                    if (!string.IsNullOrEmpty(_cleanWallpaperPath) && File.Exists(_cleanWallpaperPath))
                    {
                        try { File.Delete(_cleanWallpaperPath); } catch { }
                    }
                }

                // 3. Manage overlay windows
                foreach (var overlay in _overlayWindows)
                {
                    overlay.Close();
                }
                _overlayWindows.Clear();

                if (shouldShowOverlay)
                {
                    EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                    {
                        var rect = lprcMonitor;
                        
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var overlay = new DesktopOverlayWindow(
                                rect.Left, 
                                rect.Top, 
                                rect.Width, 
                                rect.Height, 
                                enableWallpaper ? _originalWallpaperPath : "",
                                enableIcons ? hiddenIconNames : new List<string>()
                            );
                            overlay.Show();
                            _overlayWindows.Add(overlay);
                        });

                        return true;
                    }, IntPtr.Zero);

                    _isShieldActive = true;
                }
                else
                {
                    _isShieldActive = false;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error applying Desktop Shield state: {ex.Message}", "ScreenShield Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DisableDesktopShield();
            }
        }

        public void EnableDesktopShield(string customCaptureWallpaper, List<string> hiddenIconNames)
        {
            ApplyShieldState(true, true, customCaptureWallpaper, hiddenIconNames);
        }

        public void DisableDesktopShield()
        {
            ApplyShieldState(false, false, "", null);
        }

        public void UpdateDesktopShield(string customCaptureWallpaper, List<string> hiddenIconNames)
        {
            ApplyShieldState(_isWallpaperShieldEnabled, _isIconsShieldEnabled, customCaptureWallpaper, hiddenIconNames);
        }

        private static bool IsCaptureWallpaper(string path, string customCaptureWallpaper)
        {
            if (string.IsNullOrEmpty(path)) return false;
            
            string filename = Path.GetFileName(path).ToLowerInvariant();
            if (filename.Contains("screenshield_clean_bg") || filename.Contains("screenshield_original_bg"))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(customCaptureWallpaper))
            {
                string normPath = path.Replace('/', '\\').Trim().ToLowerInvariant();
                string customPath = customCaptureWallpaper.Replace('/', '\\').Trim().ToLowerInvariant();
                if (normPath == customPath)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetCurrentRegistryWallpaper()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("Wallpaper");
                        if (value != null)
                        {
                            return value.ToString();
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        public static string GetCurrentWallpaperPath(string customCaptureWallpaper)
        {
            string backupPath = Path.Combine(Path.GetTempPath(), "screenshield_original_bg.png");

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("Wallpaper");
                        if (value != null && !string.IsNullOrEmpty(value.ToString()))
                        {
                            string path = value.ToString();
                            if (IsCaptureWallpaper(path, customCaptureWallpaper))
                            {
                                bool isBackupValid = false;
                                if (File.Exists(backupPath))
                                {
                                    try
                                    {
                                        var fi = new FileInfo(backupPath);
                                        if (fi.Length > 25000) // Solid-color background is ~5KB. Real wallpapers are >25KB.
                                        {
                                            isBackupValid = true;
                                        }
                                    }
                                    catch { }
                                }

                                if (isBackupValid)
                                {
                                    return backupPath;
                                }

                                // Self-healing: Scanning Windows Background History for a valid previous wallpaper!
                                try
                                {
                                    using (RegistryKey historyKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers"))
                                    {
                                        if (historyKey != null)
                                        {
                                            for (int i = 0; i < 5; i++)
                                            {
                                                object histVal = historyKey.GetValue($"BackgroundHistoryPath{i}");
                                                if (histVal != null && !string.IsNullOrEmpty(histVal.ToString()))
                                                {
                                                    string histPath = histVal.ToString();
                                                    if (File.Exists(histPath) && !IsCaptureWallpaper(histPath, customCaptureWallpaper))
                                                    {
                                                        File.Copy(histPath, backupPath, true);
                                                        return backupPath;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }

                                // Second fallback: try the TranscodedWallpaper cache file if its size is valid
                                try
                                {
                                    string transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                                    if (File.Exists(transcoded) && new FileInfo(transcoded).Length > 25000)
                                    {
                                        File.Copy(transcoded, backupPath, true);
                                        return backupPath;
                                    }
                                }
                                catch { }

                                if (File.Exists(backupPath))
                                {
                                    return backupPath;
                                }
                                return "";
                            }
                        }
                    }
                }
            }
            catch { }

            string sourcePath = "";

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("Wallpaper");
                        if (value != null && !string.IsNullOrEmpty(value.ToString()))
                        {
                            string path = value.ToString();
                            if (File.Exists(path) && !IsCaptureWallpaper(path, customCaptureWallpaper))
                            {
                                sourcePath = path;
                            }
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(sourcePath))
            {
                // Fallback to TranscodedWallpaper cache file (contains currently active desktop background)
                try
                {
                    string transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                    if (File.Exists(transcoded) && !IsCaptureWallpaper(transcoded, customCaptureWallpaper))
                    {
                        sourcePath = transcoded;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
            {
                try
                {
                    string tempCopy = Path.Combine(Path.GetTempPath(), "screenshield_original_bg.png");
                    File.Copy(sourcePath, tempCopy, true);
                    return tempCopy;
                }
                catch { }

                return sourcePath;
            }

            // Fallback: If current wallpaper is a ScreenShield one, try restoring from existing backup
            string existingBackup = Path.Combine(Path.GetTempPath(), "screenshield_original_bg.png");
            if (File.Exists(existingBackup))
            {
                return existingBackup;
            }

            return "";
        }

        public static bool AreDesktopIconsVisible()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("HideIcons");
                        if (value != null)
                        {
                            return (int)value == 0;
                        }
                    }
                }
            }
            catch { }

            IntPtr shellDll = GetShellDllDefViewHandle();
            if (shellDll != IntPtr.Zero)
            {
                IntPtr listView = NativeMethods.FindWindowEx(shellDll, IntPtr.Zero, "SysListView32", null);
                if (listView != IntPtr.Zero)
                {
                    return NativeMethods.IsWindowVisible(listView);
                }
            }
            return true;
        }

        public static void ToggleDesktopIcons()
        {
            IntPtr shellDll = GetShellDllDefViewHandle();
            if (shellDll != IntPtr.Zero)
            {
                NativeMethods.SendMessage(shellDll, NativeMethods.WM_COMMAND, (IntPtr)NativeMethods.TOGGLE_DESKTOP_ICONS, IntPtr.Zero);
            }
        }

        private static IntPtr GetShellDllDefViewHandle()
        {
            IntPtr progman = NativeMethods.FindWindow("Progman", "Program Manager");
            IntPtr shellDll = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shellDll == IntPtr.Zero)
            {
                IntPtr workerW = IntPtr.Zero;
                while ((workerW = NativeMethods.FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero)
                {
                    shellDll = NativeMethods.FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shellDll != IntPtr.Zero)
                        break;
                }
            }

            return shellDll;
        }

        private static void CreateSolidColorPng(string path, System.Windows.Media.Color color)
        {
            var width = 1920;
            var height = 1080;
            var pf = PixelFormats.Bgr32;
            var rawStride = (width * pf.BitsPerPixel + 7) / 8;
            var rawImage = new byte[rawStride * height];

            for (int i = 0; i < rawImage.Length; i += 4)
            {
                rawImage[i] = color.B;
                rawImage[i + 1] = color.G;
                rawImage[i + 2] = color.R;
                rawImage[i + 3] = color.A;
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, pf, null, rawImage, rawStride);
            using (var stream = new FileStream(path, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
        }
    }
}
