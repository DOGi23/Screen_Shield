using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenShield.Security;

namespace ScreenShield
{
    public class DesktopItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public ImageSource Icon { get; set; }
    }

    public partial class DesktopOverlayWindow : Window
    {
        // --- SetWindowPos P/Invoke ---
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly string _wallpaperPath;
        private readonly List<string> _hiddenIconNames;

        public DesktopOverlayWindow(int left, int top, int width, int height, string wallpaperPath, List<string> hiddenIconNames)
        {
            InitializeComponent();
            
            // 1. Position window exactly on the designated monitor
            this.Left = left;
            this.Top = top;
            this.Width = width;
            this.Height = height;
            _wallpaperPath = wallpaperPath;
            _hiddenIconNames = hiddenIconNames;

            // Subscribe to activated event to push back to bottom
            this.Activated += DesktopOverlayWindow_Activated;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Perform Anti-Debug environmental checks inside the overlay window
            AntiDebug.PerformChecks();

            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // 2. Apply display affinity (EXCLUDE FROM CAPTURE) to hide the overlay and its icons from screen capture
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            // 3. Make it a tool window (hides from Alt-Tab & Taskbar)
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

            // 4. Send to bottom of Z-order
            SendToBottom();

            // 5. Draw wallpaper background
            SetWallpaperBackground();

            // 6. Load desktop items
            LoadDesktopItems();
        }

        private void DesktopOverlayWindow_Activated(object sender, EventArgs e)
        {
            SendToBottom();
        }

        private void SendToBottom()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch { }
        }

        private void SetWallpaperBackground()
        {
            try
            {
                if (string.IsNullOrEmpty(_wallpaperPath))
                {
                    // Wallpaper Shield is disabled: make background transparent to show original Windows wallpaper
                    MainGrid.Background = System.Windows.Media.Brushes.Transparent;
                    return;
                }

                if (File.Exists(_wallpaperPath))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(_wallpaperPath);
                    image.EndInit();

                    var brush = new ImageBrush(image)
                    {
                        Stretch = Stretch.UniformToFill
                    };
                    MainGrid.Background = brush;
                }
                else
                {
                    // Wallpaper Shield is enabled but file is missing: fallback to neutral dark gray
                    MainGrid.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 36));
                }
            }
            catch
            {
                MainGrid.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 36));
            }
        }

        private void LoadDesktopItems()
        {
            if (_hiddenIconNames == null)
            {
                IconsList.ItemsSource = null;
                return;
            }

            var items = new List<DesktopItem>();

            // Folders to scan
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string publicDesktop = @"C:\Users\Public\Desktop";

            ScanFolder(userDesktop, items);
            ScanFolder(publicDesktop, items);

            // Sort items alphabetically (directories first, then files)
            var sorted = items
                .OrderBy(i => !Directory.Exists(i.Path))
                .ThenBy(i => i.Name)
                .ToList();

            IconsList.ItemsSource = sorted;
        }

        private void ScanFolder(string path, List<DesktopItem> itemsList)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                var entries = Directory.GetFileSystemEntries(path);
                foreach (var entry in entries)
                {
                    string filename = Path.GetFileName(entry);

                    // Skip system files, hidden files, and desktop.ini
                    if (filename.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || 
                        filename.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string displayName = GetDisplayName(filename);

                    // Avoid duplicate entries
                    if (itemsList.Any(i => i.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // Skip icons that are selected to be hidden by the user
                    if (IsIconHidden(filename, displayName))
                    {
                        continue;
                    }

                    ImageSource icon = GetFileIcon(entry);

                    itemsList.Add(new DesktopItem
                    {
                        Name = displayName,
                        Path = entry,
                        Icon = icon
                    });
                }
            }
            catch { }
        }

        private bool IsIconHidden(string filename, string displayName)
        {
            foreach (var hidden in _hiddenIconNames)
            {
                if (filename.Equals(hidden, StringComparison.OrdinalIgnoreCase) ||
                    displayName.Equals(hidden, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private string GetDisplayName(string filename)
        {
            if (filename.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return filename.Substring(0, filename.Length - 4);
            }
            if (filename.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                return filename.Substring(0, filename.Length - 4);
            }
            return filename;
        }

        private ImageSource GetFileIcon(string filePath)
        {
            try
            {
                var shfi = new NativeMethods.SHFILEINFO();
                IntPtr hSuccess = NativeMethods.SHGetFileInfo(
                    filePath, 
                    0, 
                    ref shfi, 
                    (uint)Marshal.SizeOf(shfi), 
                    NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON
                );

                if (shfi.hIcon != IntPtr.Zero)
                {
                    ImageSource img = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        shfi.hIcon,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions()
                    );

                    NativeMethods.DestroyIcon(shfi.hIcon);
                    return img;
                }
            }
            catch { }

            return null;
        }

        private void IconsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IconsList.SelectedItem is DesktopItem selectedItem)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = selectedItem.Path,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Could not open file: {ex.Message}", "ScreenShield", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
