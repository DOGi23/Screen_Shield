using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

// Resolve WinForms vs WPF ambiguities using explicit aliases
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using ScreenShield.Security;

namespace ScreenShield
{
    public class DesktopIconCheckItem
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public bool IsChecked { get; set; }
    }

    public partial class MainWindow : Window
    {
        private ProcessShieldManager _processManager;
        private DesktopManager _desktopManager;
        private ObservableCollection<string> _autoShieldProcesses;
        private ObservableCollection<DesktopIconCheckItem> _desktopIconCheckItems;
        private AppConfig _config;
        private bool _isAdminMode = false;
        private bool _isInitializing = true;

        // --- System Tray Icon Fields ---
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private System.Windows.Forms.ToolStripMenuItem _toggleProtectionMenuItem;
        private bool _isExitingFromTray = false;
        private bool _isGlobalProtectionActive = true;

        // --- Detachable Panels Floating Windows ---
        private FloatingPanelWindow _desktopFloatingWindow = null;
        private FloatingPanelWindow _iconsFloatingWindow = null;
        private FloatingPanelWindow _autoShieldFloatingWindow = null;
        private FloatingPanelWindow _windowsFloatingWindow = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            // 1. Check Administrator elevation status
            _isAdminMode = NativeMethods.IsAdministrator();
            AdminBadge.Visibility = _isAdminMode ? Visibility.Visible : Visibility.Collapsed;

            // 2. Load Config File
            _config = ConfigManager.LoadConfig();

            // 3. Initialize Core Managers
            _processManager = new ProcessShieldManager();
            _processManager.IsHideTaskbarEnabled = _config.IsHideTaskbarEnabled;
            _desktopManager = new DesktopManager();
            
            // Get active profile, fallback if missing
            var activeProfile = GetActiveProfile();
            
            _autoShieldProcesses = new ObservableCollection<string>(activeProfile.AutoShieldProcessNames);
            _processManager.SetAutoShieldList(activeProfile.AutoShieldProcessNames);
            _processManager.SetManuallyShieldedList(activeProfile.ManuallyShieldedProcessNames);

            AutoShieldListBox.ItemsSource = _autoShieldProcesses;

            // 4. Set Custom Wallpaper UI Value
            CustomWallpaperPathBox.Text = activeProfile.CustomWallpaperPath;

            // 5. Populate Desktop Icons Checklist
            LoadIconsChecklist(activeProfile.HiddenIconNames);

            // 6. Bind Profiles to Dropdown
            RefreshProfilesComboBox();

            // 7. Initialize System Tray Icon
            InitializeTrayIcon();

            // 8. Restore Desktop Shield and Icon Shield states if config had them enabled
            IconShieldToggle.IsChecked = activeProfile.IsIconsShieldEnabled;
            DesktopShieldToggle.IsChecked = _config.IsDesktopShieldEnabled;

            if (_config.IsDesktopShieldEnabled || activeProfile.IsIconsShieldEnabled)
            {
                _desktopManager.ApplyShieldState(
                    _config.IsDesktopShieldEnabled,
                    activeProfile.IsIconsShieldEnabled,
                    activeProfile.CustomWallpaperPath,
                    activeProfile.HiddenIconNames
                );
                UpdateDesktopUIStatus(_config.IsDesktopShieldEnabled);
                UpdateIconsUIStatus(activeProfile.IsIconsShieldEnabled);
            }
            else
            {
                UpdateDesktopUIStatus(false);
                UpdateIconsUIStatus(false);
            }

            // 8.5. Restore Hotkey settings
            HotkeyEnableToggle.IsChecked = _config.IsHotkeyEnabled;
            HotkeyRecorderButton.Content = string.IsNullOrEmpty(_config.HotkeyText) ? "Ctrl + Shift + S" : _config.HotkeyText;
            if (_config.IsHotkeyEnabled)
            {
                HotkeyEnableToggle.Content = "Активна";
                HotkeyEnableToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            }
            else
            {
                HotkeyEnableToggle.Content = "Выключена";
                HotkeyEnableToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
            }

            // 8.6. Restore Autostart settings
            bool isAutostart = IsAutostartEnabledInRegistry();
            AutostartToggle.IsChecked = isAutostart;
            if (isAutostart)
            {
                AutostartToggle.Content = "Включен";
                AutostartToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            }
            else
            {
                AutostartToggle.Content = "Выключен";
                AutostartToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
            }

            // 8.7. Restore Taskbar Hiding settings
            HideTaskbarToggle.IsChecked = _config.IsHideTaskbarEnabled;
            if (_config.IsHideTaskbarEnabled)
            {
                HideTaskbarToggle.Content = "Включено";
                HideTaskbarToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            }
            else
            {
                HideTaskbarToggle.Content = "Выключено";
                HideTaskbarToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
            }

            // 9. Perform initial scan of active windows
            RefreshWindows();

            // 10. Enable application interaction directly (Free Version)
            ToggleAppInteraction(true);

            _isInitializing = false;

            // 11. Handle minimized startup CLI argument
            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("-minimized") || args.Contains("--minimized") || args.Contains("--tray"))
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    this.WindowState = WindowState.Minimized;
                    this.Hide();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private ShieldProfile GetActiveProfile()
        {
            if (_config == null) return null;
            
            var profile = _config.Profiles.FirstOrDefault(p => p.Name.Equals(_config.CurrentProfileName, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                if (_config.Profiles.Count == 0)
                {
                    var defaultProfile = new ShieldProfile { Name = "По умолчанию" };
                    _config.Profiles.Add(defaultProfile);
                }
                profile = _config.Profiles.First();
                _config.CurrentProfileName = profile.Name;
            }
            return profile;
        }

        private void RefreshProfilesComboBox()
        {
            if (_config == null) return;

            ProfileComboBox.ItemsSource = _config.Profiles.Select(p => p.Name).ToList();
            ProfileComboBox.SelectedItem = _config.CurrentProfileName;
            
            DeleteProfileButton.IsEnabled = !_config.CurrentProfileName.Equals("По умолчанию", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshWindows()
        {
            if (_processManager == null) return;

            _processManager.RefreshWindows();
            ApplyFilter();

            int activeCount = _processManager.WindowsList.Count(w => w.IsShielded);
            ActiveShieldCountText.Text = activeCount.ToString();
        }

        private void ApplyFilter()
        {
            if (_processManager == null) return;

            string query = SearchBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(query) || query == "поиск приложений...")
            {
                WindowsListBox.ItemsSource = _processManager.WindowsList;
            }
            else
            {
                var filtered = _processManager.WindowsList
                    .Where(w => w.ProcessName.ToLowerInvariant().Contains(query) || 
                                w.WindowTitle.ToLowerInvariant().Contains(query))
                    .ToList();
                WindowsListBox.ItemsSource = filtered;
            }
        }

        // --- System Tray Icon Management ---

        private void InitializeTrayIcon()
        {
            try
            {
                _notifyIcon = new System.Windows.Forms.NotifyIcon();
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                }
                else
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Shield;
                }
                _notifyIcon.Text = "ScreenShield";
                _notifyIcon.Visible = true;

                // Setup Context Menu
                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("Открыть Dashboard", null, (s, e) =>
                {
                    ShowDashboard();
                });

                _toggleProtectionMenuItem = new System.Windows.Forms.ToolStripMenuItem("Выключить защиту", null, (s, e) =>
                {
                    Application.Current.Dispatcher.Invoke(() => ToggleGlobalProtection());
                });
                contextMenu.Items.Add(_toggleProtectionMenuItem);

                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("Выход", null, (s, e) =>
                {
                    _isExitingFromTray = true;
                    this.Close();
                });
                _notifyIcon.ContextMenuStrip = contextMenu;

                // Double Click restores dashboard
                _notifyIcon.DoubleClick += (s, e) =>
                {
                    ShowDashboard();
                };
            }
            catch { }
        }

        private void ShowDashboard()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void GlobalProtectionButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleGlobalProtection();
        }

        private void ToggleGlobalProtection(bool? forceState = null)
        {
            bool targetState = forceState.HasValue ? forceState.Value : !_isGlobalProtectionActive;
            _isGlobalProtectionActive = targetState;

            // Notify ProcessManager of the active protection state
            if (_processManager != null)
            {
                _processManager.IsGlobalProtectionActive = _isGlobalProtectionActive;
            }

            if (_isGlobalProtectionActive)
            {
                // Turn protection ON
                GlobalStatusText.Text = "Защита: Активна";
                GlobalStatusText.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120)); // Green
                
                GlobalProtectionButton.Content = "Выключить защиту";
                GlobalProtectionButton.Background = new SolidColorBrush(Color.FromRgb(160, 64, 76)); // Soft red
                GlobalProtectionButton.BorderBrush = new SolidColorBrush(Color.FromRgb(192, 80, 96));

                if (_toggleProtectionMenuItem != null)
                {
                    _toggleProtectionMenuItem.Text = "Выключить защиту";
                }

                // Restore Desktop and Icon Shields based on checkbox states
                TriggerDesktopShieldRestart();

                // Re-apply process shielding based on checked states and auto-shields
                RefreshWindows();
            }
            else
            {
                // Turn protection OFF
                GlobalStatusText.Text = "Защита: Выключена";
                GlobalStatusText.Foreground = new SolidColorBrush(Color.FromRgb(160, 64, 76)); // Soft red

                GlobalProtectionButton.Content = "Включить защиту";
                GlobalProtectionButton.Background = new SolidColorBrush(Color.FromRgb(46, 90, 68)); // Soft green
                GlobalProtectionButton.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 122, 90));

                if (_toggleProtectionMenuItem != null)
                {
                    _toggleProtectionMenuItem.Text = "Включить защиту";
                }

                // Physically unshield all windows in OS
                if (_processManager != null)
                {
                    _processManager.ResetAllShields();
                }

                // Physically disable Desktop Shield overlay and restore wallpaper/icons
                if (_desktopManager != null)
                {
                    _desktopManager.DisableDesktopShield();
                }

                // Refresh process list to display unshielded state visually in UI
                RefreshWindows();
            }

            // Play sensory audio feedback and show elegant glassmorphic toast notification
            if (!_isInitializing)
            {
                PlayAudioFeedback(_isGlobalProtectionActive);
                try
                {
                    var toast = new ToastNotificationWindow(_isGlobalProtectionActive);
                    toast.Show();
                }
                catch { }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Close to tray unless exited from tray menu
            if (!_isExitingFromTray)
            {
                e.Cancel = true;
                this.Hide();
                
                // Show a clean brief tray tip the first time
                _notifyIcon?.ShowBalloonTip(
                    2000, 
                    "ScreenShield", 
                    "Приложение скрыто в системный трей для обеспечения непрерывной защиты.", 
                    System.Windows.Forms.ToolTipIcon.Info
                );
            }
            else
            {
                // Clean exit: restore desktop and dispose icon
                UnregisterGlobalHotkey();

                if (_desktopManager != null && _desktopManager.IsShieldActive)
                {
                    _desktopManager.DisableDesktopShield();
                }

                // Close all active floating panels
                _desktopFloatingWindow?.Close();
                _iconsFloatingWindow?.Close();
                _autoShieldFloatingWindow?.Close();
                _windowsFloatingWindow?.Close();

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
        }

        // --- Profiles Management Events ---

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ProfileComboBox.SelectedItem == null) return;

            string selectedProfileName = ProfileComboBox.SelectedItem.ToString();
            
            // --- CRITICAL RESET STATE ON PROFILE SWITCH ---
            // Unshield all currently hidden windows in the OS first
            if (_processManager != null)
            {
                _processManager.ResetAllShields();
            }

            // Switch current profile
            _config.CurrentProfileName = selectedProfileName;
            var activeProfile = GetActiveProfile();

            if (_processManager != null)
            {
                _processManager.ClearManualShieldHwnds();
                _processManager.SetManuallyShieldedList(activeProfile.ManuallyShieldedProcessNames);
            }

            DeleteProfileButton.IsEnabled = !selectedProfileName.Equals("По умолчанию", StringComparison.OrdinalIgnoreCase);

            _isInitializing = true;

            // Load wallpaper path
            CustomWallpaperPathBox.Text = activeProfile.CustomWallpaperPath;

            // Load toggle states and update UI badges
            DesktopShieldToggle.IsChecked = _config.IsDesktopShieldEnabled;
            IconShieldToggle.IsChecked = activeProfile.IsIconsShieldEnabled;
            UpdateDesktopUIStatus(_config.IsDesktopShieldEnabled);
            UpdateIconsUIStatus(activeProfile.IsIconsShieldEnabled);

            // Load Auto Shield processes
            _autoShieldProcesses.Clear();
            foreach (var proc in activeProfile.AutoShieldProcessNames)
            {
                _autoShieldProcesses.Add(proc);
            }
            _processManager.SetAutoShieldList(activeProfile.AutoShieldProcessNames);

            // Load hidden icons checklist states
            foreach (var item in _desktopIconCheckItems)
            {
                bool isChecked = !activeProfile.HiddenIconNames.Contains(item.FileName) && 
                                 !activeProfile.HiddenIconNames.Contains(item.Name);
                item.IsChecked = isChecked;
            }
            IconsCheckListBox.Items.Refresh();

            // Defer resetting _isInitializing to allow data binding layout queue to drain safely
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _isInitializing = false;
                SaveCurrentConfig(); // Save after _isInitializing is set to false!
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Refresh active windows
            RefreshWindows();

            // Hot reload desktop shield if enabled
            TriggerDesktopShieldRestart();
        }

        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            NewProfileNameBox.Text = "";
            ModalOverlay.Visibility = Visibility.Visible;
            NewProfileNameBox.Focus();
        }

        private void ModalCancelProfile_Click(object sender, RoutedEventArgs e)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void ModalCreateProfile_Click(object sender, RoutedEventArgs e)
        {
            string name = NewProfileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Введите корректное имя профиля.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_config.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Профиль с таким именем уже существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var currentActive = GetActiveProfile();
            var newProfile = new ShieldProfile
            {
                Name = name,
                CustomWallpaperPath = currentActive.CustomWallpaperPath,
                AutoShieldProcessNames = new List<string>(currentActive.AutoShieldProcessNames),
                HiddenIconNames = new List<string>(currentActive.HiddenIconNames)
            };

            _config.Profiles.Add(newProfile);
            _config.CurrentProfileName = name;

            ModalOverlay.Visibility = Visibility.Collapsed;

            RefreshProfilesComboBox();
            ProfileComboBox.SelectedItem = name;

            SaveCurrentConfig();
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            string currentName = _config.CurrentProfileName;
            if (currentName.Equals("По умолчанию", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var result = MessageBox.Show(
                $"Вы уверены, что хотите удалить профиль \"{currentName}\"?",
                "Удаление профиля",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                var profile = _config.Profiles.FirstOrDefault(p => p.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));
                if (profile != null)
                {
                    _config.Profiles.Remove(profile);
                    _config.CurrentProfileName = "По умолчанию";

                    RefreshProfilesComboBox();
                    ProfileComboBox.SelectedItem = "По умолчанию";

                    SaveCurrentConfig();
                }
            }
        }

        private void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            string currentName = _config.CurrentProfileName;
            if (currentName.Equals("По умолчанию", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Нельзя переименовать профиль по умолчанию.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RenameProfileNameBox.Text = currentName;
            ModalRenameOverlay.Visibility = Visibility.Visible;
            RenameProfileNameBox.Focus();
            RenameProfileNameBox.SelectAll();
        }

        private void ModalRenameProfile_Cancel_Click(object sender, RoutedEventArgs e)
        {
            ModalRenameOverlay.Visibility = Visibility.Collapsed;
        }

        private void ModalRenameProfile_Save_Click(object sender, RoutedEventArgs e)
        {
            string newName = RenameProfileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("Введите корректное имя профиля.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string currentName = _config.CurrentProfileName;
            if (newName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
            {
                ModalRenameOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (_config.Profiles.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Профиль с таким именем уже существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var profile = _config.Profiles.FirstOrDefault(p => p.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));
            if (profile != null)
            {
                profile.Name = newName;
                _config.CurrentProfileName = newName;

                ModalRenameOverlay.Visibility = Visibility.Collapsed;

                RefreshProfilesComboBox();
                ProfileComboBox.SelectedItem = newName;

                ConfigManager.SaveConfig(_config); // Always save directly!
            }
        }

        // --- Detachable Panels / Parent Swapping Management ---

        private void DetachPanel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string key)
            {
                DetachPanel(key);
            }
        }

        private void DetachPanel(string key)
        {
            ContentControl parent = null;
            UIElement element = null;
            string title = "";
            double height = 150;

            switch (key)
            {
                case "desktop":
                    if (_desktopFloatingWindow != null) { _desktopFloatingWindow.Activate(); return; }
                    parent = DesktopShieldParent;
                    element = DesktopShieldContent;
                    title = "Управление обоями и защитой";
                    height = 140;
                    break;
                case "icons":
                    if (_iconsFloatingWindow != null) { _iconsFloatingWindow.Activate(); return; }
                    parent = IconsChecklistParent;
                    element = IconsChecklistContent;
                    title = "Ярлыки рабочего стола";
                    height = 200;
                    break;
                case "autoshield":
                    if (_autoShieldFloatingWindow != null) { _autoShieldFloatingWindow.Activate(); return; }
                    parent = AutoShieldParent;
                    element = AutoShieldContent;
                    title = "Автоматическое скрытие";
                    height = 180;
                    break;
                case "windows":
                    if (_windowsFloatingWindow != null) { _windowsFloatingWindow.Activate(); return; }
                    parent = WindowShieldParent;
                    element = WindowShieldContent;
                    title = "Окна в системе";
                    height = 400;
                    break;
            }

            if (parent == null || element == null) return;

            // 1. Remove Content from Parent on MainWindow
            parent.Content = null;

            // 2. Create Placeholder
            var placeholder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 50, 62)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(15),
                Height = height,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
            };
            var stack = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var textBlock = new TextBlock
            {
                Text = "Панель откреплена в отдельное окно.",
                Foreground = new SolidColorBrush(Color.FromRgb(128, 139, 155)),
                TextAlignment = TextAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var dockButton = new Button
            {
                Content = "Вернуть панель",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Padding = new Thickness(10, 4, 10, 4)
            };
            dockButton.Click += (s, ev) =>
            {
                // Force close floating window which automatically triggers dock back
                CloseFloatingWindow(key);
            };

            stack.Children.Add(textBlock);
            stack.Children.Add(dockButton);
            placeholder.Child = stack;

            parent.Content = placeholder;

            // 3. Spawn Floating Window
            var floatWindow = new FloatingPanelWindow(key, element, title, DockPanelBack);
            floatWindow.Owner = this;

            switch (key)
            {
                case "desktop": _desktopFloatingWindow = floatWindow; break;
                case "icons": _iconsFloatingWindow = floatWindow; break;
                case "autoshield": _autoShieldFloatingWindow = floatWindow; break;
                case "windows": _windowsFloatingWindow = floatWindow; break;
            }

            floatWindow.Show();
        }

        private void CloseFloatingWindow(string key)
        {
            switch (key)
            {
                case "desktop": _desktopFloatingWindow?.Close(); break;
                case "icons": _iconsFloatingWindow?.Close(); break;
                case "autoshield": _autoShieldFloatingWindow?.Close(); break;
                case "windows": _windowsFloatingWindow?.Close(); break;
            }
        }

        private void DockPanelBack(string key, UIElement element)
        {
            ContentControl parent = null;

            switch (key)
            {
                case "desktop":
                    parent = DesktopShieldParent;
                    _desktopFloatingWindow = null;
                    break;
                case "icons":
                    parent = IconsChecklistParent;
                    _iconsFloatingWindow = null;
                    break;
                case "autoshield":
                    parent = AutoShieldParent;
                    _autoShieldFloatingWindow = null;
                    break;
                case "windows":
                    parent = WindowShieldParent;
                    _windowsFloatingWindow = null;
                    break;
            }

            if (parent != null)
            {
                // Put content back into the MainWindow parent control
                parent.Content = element;
            }
        }

        // --- Active Window Shielding Events ---

        private void RefreshWindows_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindows();
        }

        private void SelectAllWindows_Click(object sender, RoutedEventArgs e)
        {
            if (_processManager == null) return;

            var activeProfile = GetActiveProfile();
            bool hasFailures = false;

            foreach (var winInfo in _processManager.WindowsList)
            {
                var procName = winInfo.ProcessName.ToLowerInvariant();
                if (!activeProfile.ManuallyShieldedProcessNames.Contains(procName))
                {
                    activeProfile.ManuallyShieldedProcessNames.Add(procName);
                    _processManager.AddManualShieldProcess(procName);
                }

                bool success = _processManager.SetShieldState(winInfo.Hwnd, true);
                if (!success)
                {
                    hasFailures = true;
                }
            }

            SaveCurrentConfig();
            RefreshWindows();

            if (hasFailures && _isGlobalProtectionActive)
            {
                MessageBox.Show(
                    "Некоторые окна не удалось скрыть. Возможно, они запущены от Администратора (запустите ScreenShield от Администратора) или защищены системой.",
                    "Предупреждение",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void UnselectAllWindows_Click(object sender, RoutedEventArgs e)
        {
            if (_processManager == null) return;

            var activeProfile = GetActiveProfile();
            activeProfile.ManuallyShieldedProcessNames.Clear();
            _processManager.SetManuallyShieldedList(null);

            var hwnds = _processManager.WindowsList.Select(w => w.Hwnd).ToList();
            foreach (var hwnd in hwnds)
            {
                _processManager.SetShieldState(hwnd, false);
            }

            SaveCurrentConfig();
            RefreshWindows();
        }

        private void WindowShieldToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && checkbox.Tag is IntPtr hwnd)
            {
                var winInfo = _processManager.WindowsList.FirstOrDefault(w => w.Hwnd == hwnd);
                if (winInfo != null)
                {
                    var activeProfile = GetActiveProfile();
                    var procName = winInfo.ProcessName.ToLowerInvariant();
                    if (!activeProfile.ManuallyShieldedProcessNames.Contains(procName))
                    {
                        activeProfile.ManuallyShieldedProcessNames.Add(procName);
                        _processManager.AddManualShieldProcess(procName);
                        SaveCurrentConfig();
                    }
                }

                bool success = _processManager.SetShieldState(hwnd, true);
                if (!success && _isGlobalProtectionActive)
                {
                    checkbox.IsChecked = false;
                    MessageBox.Show(
                        "Не удалось скрыть окно. Возможно, оно запущено от Администратора " +
                        "(запустите ScreenShield от Администратора) или защищены системой.",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }
                RefreshWindows();
            }
        }

        private void WindowShieldToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && checkbox.Tag is IntPtr hwnd)
            {
                var winInfo = _processManager.WindowsList.FirstOrDefault(w => w.Hwnd == hwnd);
                if (winInfo != null)
                {
                    var activeProfile = GetActiveProfile();
                    var procName = winInfo.ProcessName.ToLowerInvariant();
                    activeProfile.ManuallyShieldedProcessNames.Remove(procName);
                    _processManager.RemoveManualShieldProcess(procName);
                    SaveCurrentConfig();
                }

                _processManager.SetShieldState(hwnd, false);
                RefreshWindows();
            }
        }

        // --- Desktop Shielding Events ---

        private void DesktopShieldToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_desktopManager == null || _isInitializing) return;

            UpdateDesktopUIStatus(true);
            SaveCurrentConfig();

            if (!_isGlobalProtectionActive)
            {
                MessageBox.Show(
                    "Глобальная защита выключена. Изменения сохранены, но защита рабочего стола станет активна только после включения глобальной защиты.",
                    "Внимание",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            TriggerDesktopShieldRestart();
        }

        private void DesktopShieldToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_desktopManager == null || _isInitializing) return;

            UpdateDesktopUIStatus(false);
            SaveCurrentConfig();

            TriggerDesktopShieldRestart();
        }

        private void UpdateDesktopUIStatus(bool enabled)
        {
            _config.IsDesktopShieldEnabled = enabled;
            if (enabled)
            {
                DesktopShieldToggle.Content = "Активна";
                DesktopShieldToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            }
            else
            {
                DesktopShieldToggle.Content = "Включить";
                DesktopShieldToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
            }
        }

        private void IconShieldToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_desktopManager == null || _isInitializing) return;

            UpdateIconsUIStatus(true);
            SaveCurrentConfig();

            if (!_isGlobalProtectionActive)
            {
                MessageBox.Show(
                    "Глобальная защита выключена. Изменения сохранены, но скрытие ярлыков станет активно только после включения глобальной защиты.",
                    "Внимание",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            TriggerDesktopShieldRestart();
        }

        private void IconShieldToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_desktopManager == null || _isInitializing) return;

            UpdateIconsUIStatus(false);
            SaveCurrentConfig();

            TriggerDesktopShieldRestart();
        }

        private void UpdateIconsUIStatus(bool enabled)
        {
            var activeProfile = GetActiveProfile();
            if (activeProfile != null)
            {
                activeProfile.IsIconsShieldEnabled = enabled;
            }

            if (enabled)
            {
                IconShieldToggle.Content = "Активна";
                IconShieldToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            }
            else
            {
                IconShieldToggle.Content = "Включить";
                IconShieldToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
            }
        }

        // --- Wallpaper Browsing ---

        private void BrowseWallpaper_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Все файлы (*.*)|*.*",
                Title = "Выберите обои для записи (то, что увидит OBS)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var activeProfile = GetActiveProfile();
                activeProfile.CustomWallpaperPath = openFileDialog.FileName;
                CustomWallpaperPathBox.Text = openFileDialog.FileName;
                
                SaveCurrentConfig();

                TriggerDesktopShieldRestart();
            }
        }

        private void ResetWallpaper_Click(object sender, RoutedEventArgs e)
        {
            var activeProfile = GetActiveProfile();
            activeProfile.CustomWallpaperPath = "";
            CustomWallpaperPathBox.Text = "";
            
            SaveCurrentConfig();

            TriggerDesktopShieldRestart();
        }

        private void TriggerDesktopShieldRestart()
        {
            if (_desktopManager != null)
            {
                var activeProfile = GetActiveProfile();

                if (_isGlobalProtectionActive)
                {
                    _desktopManager.ApplyShieldState(
                        _config.IsDesktopShieldEnabled,
                        activeProfile.IsIconsShieldEnabled,
                        activeProfile.CustomWallpaperPath,
                        activeProfile.HiddenIconNames
                    );
                }
                else
                {
                    _desktopManager.DisableDesktopShield();
                }
            }
        }

        // --- Desktop Icons Visibility Checklist ---

        private void LoadIconsChecklist(List<string> hiddenIconNames)
        {
            var list = new List<DesktopIconCheckItem>();
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string publicDesktop = @"C:\Users\Public\Desktop";

            ScanFolderForChecklist(userDesktop, list, hiddenIconNames);
            ScanFolderForChecklist(publicDesktop, list, hiddenIconNames);

            var sorted = list.OrderBy(i => i.Name).ToList();
            _desktopIconCheckItems = new ObservableCollection<DesktopIconCheckItem>(sorted);
            IconsCheckListBox.ItemsSource = _desktopIconCheckItems;
        }

        private void ScanFolderForChecklist(string path, List<DesktopIconCheckItem> list, List<string> hiddenIconNames)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                var entries = Directory.GetFileSystemEntries(path);
                foreach (var entry in entries)
                {
                    string filename = Path.GetFileName(entry);

                    if (filename.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || 
                        filename.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string displayName = filename;
                    if (filename.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || 
                        filename.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = filename.Substring(0, filename.Length - 4);
                    }

                    if (list.Any(i => i.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    bool isChecked = !hiddenIconNames.Contains(filename) && !hiddenIconNames.Contains(displayName);

                    list.Add(new DesktopIconCheckItem
                    {
                        Name = displayName,
                        FileName = filename,
                        IsChecked = isChecked
                    });
                }
            }
            catch { }
        }

        private void IconCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if (_desktopIconCheckItems == null) return;

            var hidden = new List<string>();
            foreach (var item in _desktopIconCheckItems)
            {
                if (!item.IsChecked)
                {
                    hidden.Add(item.FileName);
                }
            }

            var activeProfile = GetActiveProfile();
            activeProfile.HiddenIconNames = hidden;
            
            ConfigManager.SaveConfig(_config); // Always save directly!

            TriggerDesktopShieldRestart();
        }

        private void SelectAllIcons_Click(object sender, RoutedEventArgs e)
        {
            ToggleAllIconsCheckedState(true);
        }

        private void UnselectAllIcons_Click(object sender, RoutedEventArgs e)
        {
            ToggleAllIconsCheckedState(false);
        }

        private void ToggleAllIconsCheckedState(bool check)
        {
            if (_desktopIconCheckItems == null) return;

            foreach (var item in _desktopIconCheckItems)
            {
                item.IsChecked = check;
            }
            IconsCheckListBox.Items.Refresh();

            var hidden = new List<string>();
            foreach (var item in _desktopIconCheckItems)
            {
                if (!item.IsChecked)
                {
                    hidden.Add(item.FileName);
                }
            }

            var activeProfile = GetActiveProfile();
            activeProfile.HiddenIconNames = hidden;
            
            ConfigManager.SaveConfig(_config); // Always save directly!

            TriggerDesktopShieldRestart();
        }

        // --- Auto-Shield Management ---

        private void AddAutoShield_Click(object sender, RoutedEventArgs e)
        {
            string name = AutoShieldInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            AddAutoShieldProcess(name);
            AutoShieldInput.Text = "";
        }

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
                Title = "Выберите приложение (.exe) для авто-скрытия"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string procName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                AddAutoShieldProcess(procName);
            }
        }

        private void AddAutoShieldProcess(string procName)
        {
            string normalized = procName.ToLowerInvariant().Replace(".exe", "").Trim();
            if (string.IsNullOrEmpty(normalized)) return;

            if (!_autoShieldProcesses.Contains(normalized))
            {
                _autoShieldProcesses.Add(normalized);
                _processManager.AddAutoShieldProcess(normalized);
                
                var activeProfile = GetActiveProfile();
                activeProfile.AutoShieldProcessNames = _autoShieldProcesses.ToList();
                
                SaveCurrentConfig();

                RefreshWindows();
            }
        }

        private void RemoveAutoShield_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string name)
            {
                _autoShieldProcesses.Remove(name);
                _processManager.RemoveAutoShieldProcess(name);

                var activeProfile = GetActiveProfile();
                activeProfile.AutoShieldProcessNames = _autoShieldProcesses.ToList();
                
                SaveCurrentConfig();
            }
        }

        private void SaveCurrentConfig()
        {
            if (_isInitializing) return;
            ConfigManager.SaveConfig(_config);
        }

        // --- Search Filters ---

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Поиск приложений...")
            {
                SearchBox.Text = "";
                SearchBox.Foreground = new SolidColorBrush(Colors.White);
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Поиск приложений...";
                SearchBox.Foreground = new SolidColorBrush(Color.FromRgb(96, 107, 123));
            }
        }

        // --- Win32 Global Hotkey Management ---
        private System.Windows.Interop.HwndSource _hwndSource;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9000;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
                _hwndSource.AddHook(HwndHook);
                
                RegisterGlobalHotkey();
            }
            catch { }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, System.IntPtr wParam, System.IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleGlobalProtection();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void RegisterGlobalHotkey()
        {
            UnregisterGlobalHotkey();

            if (_config == null || !_config.IsHotkeyEnabled) return;

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                if (hwnd != IntPtr.Zero)
                {
                    uint modifiers = (uint)_config.HotkeyModifiers;
                    uint key = (uint)_config.HotkeyKey;

                    if (key != 0)
                    {
                        NativeMethods.RegisterHotKey(hwnd, HOTKEY_ID, modifiers, key);
                    }
                }
            }
            catch { }
        }

        private void UnregisterGlobalHotkey()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.UnregisterHotKey(hwnd, HOTKEY_ID);
                }
            }
            catch { }
        }

        // --- Hotkey Recorder Event Handlers ---
        private bool _isRecordingHotkey = false;
        private int _recordedModifiers = 0;
        private int _recordedKey = 0;
        private string _recordedText = "";

        private void HotkeyRecorderButton_Click(object sender, RoutedEventArgs e)
        {
            StartHotkeyRecording();
        }

        private void StartHotkeyRecording()
        {
            _isRecordingHotkey = true;
            HotkeyRecorderButton.Content = "Нажмите клавиши...";
            HotkeyRecorderButton.Background = new SolidColorBrush(Color.FromRgb(46, 33, 37)); // soft red/pink background
            HotkeyRecorderButton.BorderBrush = new SolidColorBrush(Color.FromRgb(144, 64, 76));
            HotkeyRecorderButton.Focus();
        }

        private void HotkeyRecorderButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecordingHotkey) return;

            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                StopHotkeyRecording(false);
                return;
            }

            var modifiers = Keyboard.Modifiers;
            
            // If it's a modifier key itself, wait for a non-modifier key
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                string progressText = "";
                if ((modifiers & ModifierKeys.Control) != 0) progressText += "Ctrl + ";
                if ((modifiers & ModifierKeys.Alt) != 0) progressText += "Alt + ";
                if ((modifiers & ModifierKeys.Shift) != 0) progressText += "Shift + ";
                if ((modifiers & ModifierKeys.Windows) != 0) progressText += "Win + ";
                
                HotkeyRecorderButton.Content = progressText + "...";
                return;
            }

            int win32Modifiers = 0;
            string displayText = "";

            if ((modifiers & ModifierKeys.Control) != 0)
            {
                win32Modifiers |= 2; // MOD_CONTROL
                displayText += "Ctrl + ";
            }
            if ((modifiers & ModifierKeys.Alt) != 0)
            {
                win32Modifiers |= 1; // MOD_ALT
                displayText += "Alt + ";
            }
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                win32Modifiers |= 4; // MOD_SHIFT
                displayText += "Shift + ";
            }
            if ((modifiers & ModifierKeys.Windows) != 0)
            {
                win32Modifiers |= 8; // MOD_WIN
                displayText += "Win + ";
            }

            string keyText = e.Key.ToString();
            displayText += keyText;

            int vkCode = KeyInterop.VirtualKeyFromKey(e.Key);

            _recordedModifiers = win32Modifiers;
            _recordedKey = vkCode;
            _recordedText = displayText;

            StopHotkeyRecording(true);
        }

        private void StopHotkeyRecording(bool success)
        {
            _isRecordingHotkey = false;
            
            HotkeyRecorderButton.Background = new SolidColorBrush(Color.FromRgb(26, 26, 32)); // #1A1A20
            HotkeyRecorderButton.BorderBrush = new SolidColorBrush(Color.FromRgb(61, 68, 84)); // #3D4454

            if (success)
            {
                _config.HotkeyModifiers = _recordedModifiers;
                _config.HotkeyKey = _recordedKey;
                _config.HotkeyText = _recordedText;
                
                HotkeyRecorderButton.Content = _config.HotkeyText;
                
                SaveCurrentConfig();
                RegisterGlobalHotkey();
            }
            else
            {
                HotkeyRecorderButton.Content = _config.HotkeyText;
            }

            Keyboard.ClearFocus();
        }

        private void HotkeyEnableToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _config == null) return;
            _config.IsHotkeyEnabled = true;
            HotkeyEnableToggle.Content = "Активна";
            HotkeyEnableToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            SaveCurrentConfig();
            RegisterGlobalHotkey();
        }

        private void HotkeyEnableToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _config == null) return;
            _config.IsHotkeyEnabled = false;
            HotkeyEnableToggle.Content = "Выключена";
            HotkeyEnableToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
            SaveCurrentConfig();
            UnregisterGlobalHotkey();
        }

        private void ToggleAppInteraction(bool isEnabled)
        {
            GlobalProtectionButton.IsEnabled = true;
            DesktopShieldToggle.IsEnabled = true;
            IconShieldToggle.IsEnabled = true;
            HotkeyRecorderButton.IsEnabled = true;
            HotkeyEnableToggle.IsEnabled = true;
            ProfileComboBox.IsEnabled = true;

            // If hotkey was enabled in config, register it
            if (_config != null && _config.IsHotkeyEnabled)
            {
                RegisterGlobalHotkey();
            }
        }

        // --- Sensory Audio Feedback Chimes ---
        private void PlayAudioFeedback(bool isEnabled)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (isEnabled)
                    {
                        Console.Beep(880, 80);
                        Console.Beep(1200, 120);
                    }
                    else
                    {
                        Console.Beep(988, 80);
                        Console.Beep(659, 120);
                    }
                }
                catch { }
            });
        }

        // --- Windows Startup Autostart Management ---
        private bool IsAutostartEnabledInRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("ScreenShield");
                        return val != null;
                    }
                }
            }
            catch { }
            return false;
        }

        private void SetAutostart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Process.GetCurrentProcess().MainModule.FileName;
                            key.SetValue("ScreenShield", $"\"{exePath}\" -minimized");
                        }
                        else
                        {
                            key.DeleteValue("ScreenShield", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось настроить автозагрузку в реестре: {ex.Message}", 
                    "Ошибка реестра", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AutostartToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SetAutostart(true);
            AutostartToggle.Content = "Включен";
            AutostartToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
        }

        private void AutostartToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SetAutostart(false);
            AutostartToggle.Content = "Выключен";
            AutostartToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
        }

        private void HideTaskbarToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _config == null) return;
            _config.IsHideTaskbarEnabled = true;
            HideTaskbarToggle.Content = "Включено";
            HideTaskbarToggle.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            SaveCurrentConfig();

            if (_processManager != null)
            {
                _processManager.IsHideTaskbarEnabled = true;
                _processManager.RefreshWindows(); // Re-apply
            }
        }

        private void HideTaskbarToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _config == null) return;
            _config.IsHideTaskbarEnabled = false;
            HideTaskbarToggle.Content = "Выключено";
            HideTaskbarToggle.Foreground = new SolidColorBrush(Color.FromRgb(208, 214, 226));
            SaveCurrentConfig();

            if (_processManager != null)
            {
                _processManager.IsHideTaskbarEnabled = false;
                _processManager.ResetAllShields();
                _processManager.RefreshWindows();
            }
        }

        // --- Drag & Drop Custom Wallpaper Support ---
        private void WallpaperPanel_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                WallpaperDropPanel.Background = new SolidColorBrush(Color.FromArgb(40, 80, 200, 120)); // glowing green tint
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void WallpaperPanel_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            WallpaperDropPanel.Background = System.Windows.Media.Brushes.Transparent;
            e.Handled = true;
        }

        private void WallpaperPanel_Drop(object sender, System.Windows.DragEventArgs e)
        {
            WallpaperDropPanel.Background = System.Windows.Media.Brushes.Transparent;
            
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                    {
                        var activeProfile = GetActiveProfile();
                        activeProfile.CustomWallpaperPath = filePath;
                        CustomWallpaperPathBox.Text = filePath;
                        
                        SaveCurrentConfig();
                        TriggerDesktopShieldRestart();
                        
                        // Play a happy success chime!
                        PlayAudioFeedback(true);
                    }
                    else
                    {
                        MessageBox.Show("Пожалуйста, перетащите корректное изображение (PNG, JPG, JPEG или BMP).", 
                            "Формат не поддерживается", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            e.Handled = true;
        }
    }
}