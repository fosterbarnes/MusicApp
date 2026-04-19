using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace musicApp.Views
{
    public partial class EmptyLibraryAddOverlay : UserControl
    {
        public EmptyLibraryAddOverlay()
        {
            InitializeComponent();
        }

        public event EventHandler? AddMusicFolderRequested;

        public static bool IsTrackLibraryEmpty(IEnumerable? source)
        {
            if (source == null)
                return true;
            if (source is ICollection col)
                return col.Count == 0;
            var e = source.GetEnumerator();
            try
            {
                return !e.MoveNext();
            }
            finally
            {
                if (e is IDisposable d)
                    d.Dispose();
            }
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AddMusicFolderRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
