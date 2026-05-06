using System;

namespace musicApp.Hotkey
{
    public class GlobalHotkeys
    {
        private MainWindow _mainWindow;

        public GlobalHotkeys(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            // Global hotkeys that work with the app unfocused will be registered here.
        }
    }
}
