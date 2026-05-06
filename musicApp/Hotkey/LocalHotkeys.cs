using System.Windows.Input;
using System.Windows.Controls;
using System;

namespace musicApp.Hotkey
{
    public class LocalHotkeys
    {
        private MainWindow _mainWindow;

        public LocalHotkeys(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _mainWindow.PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ignore hotkeys if user is interacting with textboxes or sliders
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is Slider)
                return;

            // Prevent multiple triggers if key is held down
            if (e.IsRepeat)
                return;

            if (e.Key == Key.Space)
            {
                e.Handled = true;
                _mainWindow.HandlePlayPauseHotkey();
            }
            else if (e.Key == Key.Left)
            {
                e.Handled = true;
                _mainWindow.HandlePreviousTrackHotkey();
            }
            else if (e.Key == Key.Right)
            {
                e.Handled = true;
                _mainWindow.HandleNextTrackHotkey();
            }
            else if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                _mainWindow.HandlePlaySelectedTrackHotkey();
            }
        }
    }
}
