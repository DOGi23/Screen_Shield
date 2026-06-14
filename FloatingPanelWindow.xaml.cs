using System;
using System.ComponentModel;
using System.Windows;

namespace ScreenShield
{
    public partial class FloatingPanelWindow : Window
    {
        private readonly string _panelKey;
        private readonly UIElement _contentElement;
        private readonly Action<string, UIElement> _onDockAction;
        private bool _isDocked = false;

        public FloatingPanelWindow(string panelKey, UIElement contentElement, string title, Action<string, UIElement> onDockAction)
        {
            InitializeComponent();

            _panelKey = panelKey;
            _contentElement = contentElement;
            _onDockAction = onDockAction;

            TitleText.Text = title.ToUpperInvariant();
            this.Title = title;

            // Load content element into area
            ContentArea.Content = _contentElement;
        }

        private void DockButton_Click(object sender, RoutedEventArgs e)
        {
            DockBack();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_isDocked)
            {
                DockBack();
            }
        }

        private void DockBack()
        {
            _isDocked = true;

            // 1. Remove content from the presenter so it can be re-parented
            ContentArea.Content = null;

            // 2. Invoke the callback on MainWindow to dock it back
            try
            {
                _onDockAction?.Invoke(_panelKey, _contentElement);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка при возврате панели: {ex.Message}", "ScreenShield", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 3. Close the window
            this.Close();
        }
    }
}
