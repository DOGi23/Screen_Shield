using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenShield;

namespace ScreenShield
{
    public class ProcessWindowInfo
    {
        public IntPtr Hwnd { get; set; }
        public uint ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string WindowTitle { get; set; }
        public bool IsShielded { get; set; }
        public bool IsElevated { get; set; }
        public ImageSource Icon { get; set; }
        public string DisplayName => $"{ProcessName} - {WindowTitle}";
    }
}

namespace ScreenShield.Security
{
    public class ProcessShieldManager
    {
        private readonly List<string> _autoShieldProcessNames = new List<string>();
        private readonly List<string> _manuallyShieldedProcessNames = new List<string>();
        private readonly HashSet<IntPtr> _manuallyShieldedHwnds = new HashSet<IntPtr>();
        private readonly DispatcherTimer _monitorTimer;
        private readonly HashSet<IntPtr> _currentlyShieldedWindows = new HashSet<IntPtr>();

        public bool IsGlobalProtectionActive { get; set; } = true;
        public bool IsHideTaskbarEnabled { get; set; } = false;

        public ObservableCollection<ProcessWindowInfo> WindowsList { get; } = new ObservableCollection<ProcessWindowInfo>();

        public void SetManuallyShieldedList(IEnumerable<string> list)
        {
            _manuallyShieldedProcessNames.Clear();
            if (list != null)
            {
                foreach (var item in list)
                {
                    AddManualShieldProcess(item);
                }
            }
        }

        public void AddManualShieldProcess(string processName)
        {
            var normalized = processName.ToLowerInvariant().Replace(".exe", "").Trim();
            if (!string.IsNullOrEmpty(normalized) && !_manuallyShieldedProcessNames.Contains(normalized))
            {
                _manuallyShieldedProcessNames.Add(normalized);
            }
        }

        public void RemoveManualShieldProcess(string processName)
        {
            var normalized = processName.ToLowerInvariant().Replace(".exe", "").Trim();
            _manuallyShieldedProcessNames.Remove(normalized);
        }

        public List<string> GetManualShieldList() => _manuallyShieldedProcessNames.ToList();

        public void ClearManualShieldHwnds()
        {
            _manuallyShieldedHwnds.Clear();
        }

        public ProcessShieldManager()
        {
            // Set up background monitoring for auto-shielding newly opened windows
            _monitorTimer = new DispatcherTimer();
            _monitorTimer.Interval = TimeSpan.FromSeconds(1.5);
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();
        }

        public void AddAutoShieldProcess(string processName)
        {
            var normalized = processName.ToLowerInvariant().Replace(".exe", "").Trim();
            if (!string.IsNullOrEmpty(normalized) && !_autoShieldProcessNames.Contains(normalized))
            {
                _autoShieldProcessNames.Add(normalized);
            }
        }

        public void RemoveAutoShieldProcess(string processName)
        {
            var normalized = processName.ToLowerInvariant().Replace(".exe", "").Trim();
            _autoShieldProcessNames.Remove(normalized);
        }

        public List<string> GetAutoShieldList() => _autoShieldProcessNames.ToList();

        public void SetAutoShieldList(IEnumerable<string> list)
        {
            _autoShieldProcessNames.Clear();
            if (list != null)
            {
                foreach (var item in list)
                {
                    AddAutoShieldProcess(item);
                }
            }
        }

        public void RefreshWindows()
        {
            WindowsList.Clear();
            var foundWindows = new List<ProcessWindowInfo>();
            var currentPid = (uint)Process.GetCurrentProcess().Id;

            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                // Only visible windows
                if (!NativeMethods.IsWindowVisible(hwnd))
                    return true;

                // Only windows with a title
                int len = NativeMethods.GetWindowTextLength(hwnd);
                if (len == 0)
                    return true;

                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();

                // Skip common system components, overlay windows, and this app
                if (string.IsNullOrWhiteSpace(title) ||
                    title == "Program Manager" ||
                    title == "Settings" ||
                    title == "Microsoft Text Input Application" ||
                    title == "ScreenShield" || 
                    title == "DesktopShieldOverlay")
                {
                    return true;
                }

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0 || pid == currentPid)
                    return true;

                string procName = "Unknown";
                bool isElevated = false;
                string exePath = "";

                try
                {
                    using (var proc = Process.GetProcessById((int)pid))
                    {
                        procName = proc.ProcessName;
                    }
                }
                catch
                {
                    isElevated = true;
                    procName = GetProcessNameFromPidFallback(pid);
                }

                // Resolve full executable path (works for elevated apps too if using PROCESS_QUERY_LIMITED_INFORMATION)
                exePath = GetProcessExecutablePath(pid);

                // Extract Icon from executable
                ImageSource iconSource = GetProcessIcon(exePath);

                uint affinity;
                bool isShielded = false;
                if (NativeMethods.GetWindowDisplayAffinity(hwnd, out affinity))
                {
                    isShielded = (affinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                }

                bool shouldBeShielded = isShielded || 
                                        _manuallyShieldedHwnds.Contains(hwnd) || 
                                        _autoShieldProcessNames.Contains(procName.ToLowerInvariant()) || 
                                        _manuallyShieldedProcessNames.Contains(procName.ToLowerInvariant());

                if (shouldBeShielded)
                {
                    _manuallyShieldedHwnds.Add(hwnd);

                    if (IsGlobalProtectionActive && !isShielded)
                    {
                        if (NativeMethods.SetForeignWindowAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE))
                        {
                            isShielded = true;
                        }
                    }
                }

                if (!IsGlobalProtectionActive && isShielded)
                {
                    NativeMethods.SetForeignWindowAffinity(hwnd, NativeMethods.WDA_NONE);
                    isShielded = false;
                }

                foundWindows.Add(new ProcessWindowInfo
                {
                    Hwnd = hwnd,
                    ProcessId = pid,
                    ProcessName = procName,
                    WindowTitle = title,
                    IsShielded = shouldBeShielded,
                    IsElevated = isElevated,
                    Icon = iconSource
                });

                return true;
            }, IntPtr.Zero);

            // Sort by shielded first, then process name
            var sorted = foundWindows
                .OrderByDescending(w => w.IsShielded)
                .ThenBy(w => w.ProcessName)
                .ToList();

            foreach (var win in sorted)
            {
                WindowsList.Add(win);
                if (win.IsShielded)
                {
                    _currentlyShieldedWindows.Add(win.Hwnd);
                    if (IsGlobalProtectionActive && IsHideTaskbarEnabled)
                    {
                        NativeMethods.DeleteWindowFromTaskbar(win.Hwnd);
                    }
                }
                else
                {
                    _currentlyShieldedWindows.Remove(win.Hwnd);
                    if (IsHideTaskbarEnabled)
                    {
                        NativeMethods.AddWindowToTaskbar(win.Hwnd);
                    }
                }
            }
        }

        public bool SetShieldState(IntPtr hwnd, bool shield)
        {
            uint affinity = shield ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
            bool success = true;

            if (IsGlobalProtectionActive)
            {
                success = NativeMethods.SetForeignWindowAffinity(hwnd, affinity);
            }

            if (success)
            {
                if (shield)
                {
                    _currentlyShieldedWindows.Add(hwnd);
                    _manuallyShieldedHwnds.Add(hwnd);
                    if (IsHideTaskbarEnabled)
                    {
                        NativeMethods.DeleteWindowFromTaskbar(hwnd);
                    }
                }
                else
                {
                    _currentlyShieldedWindows.Remove(hwnd);
                    _manuallyShieldedHwnds.Remove(hwnd);
                    if (IsHideTaskbarEnabled)
                    {
                        NativeMethods.AddWindowToTaskbar(hwnd);
                    }
                }
                
                // Update item in the list
                var item = WindowsList.FirstOrDefault(w => w.Hwnd == hwnd);
                if (item != null)
                {
                    item.IsShielded = shield;
                }
            }

            return success;
        }

        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            if (!IsGlobalProtectionActive) return;

            if (IsHideTaskbarEnabled)
            {
                foreach (var hwnd in _currentlyShieldedWindows)
                {
                    NativeMethods.DeleteWindowFromTaskbar(hwnd);
                }
            }

            if (_autoShieldProcessNames.Count == 0 && _manuallyShieldedProcessNames.Count == 0) return;

            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd) || _currentlyShieldedWindows.Contains(hwnd))
                    return true;

                int len = NativeMethods.GetWindowTextLength(hwnd);
                if (len == 0) return true;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return true;

                string procName = "";
                try
                {
                    using (var proc = Process.GetProcessById((int)pid))
                    {
                        procName = proc.ProcessName.ToLowerInvariant();
                    }
                }
                catch
                {
                    procName = GetProcessNameFromPidFallback(pid).ToLowerInvariant();
                }

                if (_autoShieldProcessNames.Contains(procName) || _manuallyShieldedProcessNames.Contains(procName))
                {
                    // Auto shield this window!
                    SetShieldState(hwnd, true);
                }

                return true;
            }, IntPtr.Zero);
        }

        public void ResetAllShields()
        {
            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                uint affinity;
                if (NativeMethods.GetWindowDisplayAffinity(hwnd, out affinity))
                {
                    if (affinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE)
                    {
                        NativeMethods.SetForeignWindowAffinity(hwnd, NativeMethods.WDA_NONE);
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (IsHideTaskbarEnabled)
            {
                foreach (var hwnd in _currentlyShieldedWindows)
                {
                    NativeMethods.AddWindowToTaskbar(hwnd);
                }
            }
            _currentlyShieldedWindows.Clear();
        }

        private string GetProcessNameFromPidFallback(uint pid)
        {
            try
            {
                var processes = Process.GetProcesses();
                var proc = processes.FirstOrDefault(p => p.Id == pid);
                if (proc != null)
                {
                    return proc.ProcessName;
                }
            }
            catch { }
            return "Elevated Process";
        }

        private string GetProcessExecutablePath(uint pid)
        {
            IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    int size = 2048;
                    var sb = new StringBuilder(size);
                    if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
            return "";
        }

        private ImageSource GetProcessIcon(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return null;

            try
            {
                var shfi = new NativeMethods.SHFILEINFO();
                IntPtr hSuccess = NativeMethods.SHGetFileInfo(
                    exePath,
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
    }
}
