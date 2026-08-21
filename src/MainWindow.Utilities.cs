using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DisplayDeckApp
{
    internal sealed partial class MainWindow : Window
    {
        private void OpenSettings()
        {
            SettingsDialog dialog = new SettingsDialog(settings, ProfileStore.GetOrderedProfiles());
            dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;
            settings = dialog.Settings;
            SettingsStore.Save(settings);
            SettingsStore.SetStartWithWindows(dialog.StartWithWindows);
            RefreshProfiles();
            SetStatus("Settings saved", true);
        }

        private void OpenDiagnostics()
        {
            DiagnosticsDialog dialog = new DiagnosticsDialog(ProfileStore.GetOrderedProfiles());
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(AppPaths.SwitchLogFile)) File.WriteAllText(AppPaths.SwitchLogFile, "No NVAPI operations have been logged yet.\r\n");
                Process.Start("notepad.exe", AppPaths.SwitchLogFile);
            }
            catch { }
        }

        private void SetBusy(bool value, string message)
        {
            busy = value;
            if (captureButton != null) captureButton.IsEnabled = !value;
            SetStatus(message, !value);
        }

        private void SetStatus(string message, bool good)
        {
            if (statusText == null) return;
            statusText.Text = message;
            statusText.Foreground = good ? Ui.Brush("AccentBrush", "#76B900") : Ui.Brush("WarningBrush", "#E4B362");
        }

        private void ShowError(string title, string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) detail = "An unknown error occurred.";
            System.Windows.MessageBox.Show(this, detail, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
