using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenShield
{
    /// <summary>
    /// Interaction logic for ToastNotificationWindow.xaml
    /// </summary>
    public partial class ToastNotificationWindow : Window
    {
        private readonly bool _isActive;

        public ToastNotificationWindow(bool isActive)
        {
            InitializeComponent();
            _isActive = isActive;
            
            if (isActive)
            {
                ToastBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 120)); // glowing emerald green
                ToastIcon.Text = "🛡️";
                ToastTitle.Text = "ЗАЩИТА АКТИВИРОВАНА";
                ToastTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 200, 120));
                ToastMessage.Text = "Окна и рабочий стол полностью скрыты от захвата экрана.";
            }
            else
            {
                ToastBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(192, 80, 96)); // glowing soft crimson red
                ToastIcon.Text = "🔓";
                ToastTitle.Text = "ЗАЩИТА ОТКЛЮЧЕНА";
                ToastTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(192, 80, 96));
                ToastMessage.Text = "Экранные фильтры деактивированы. Запись разрешена.";
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Position at the bottom-right corner of the work area
                var workArea = SystemParameters.WorkArea;
                this.Left = workArea.Right - this.Width - 20;
                this.Top = workArea.Bottom - this.Height - 20;

                // Play the Storyboard and close the window upon completion
                var sb = (Storyboard)FindResource("FadeInAndOut");
                sb.Completed += (s, ev) => this.Close();
                sb.Begin(this);
            }
            catch
            {
                // Safe fallback close in case of positioning or resources errors
                this.Close();
            }
        }
    }
}
