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
        private void ToolsButton_Click(object sender, RoutedEventArgs e)
        {
            Button anchor = sender as Button;
            if (anchor == null) return;
            ContextMenu menu = new ContextMenu();
            menu.Background = Ui.Brush("SurfaceBrush", "#101813");
            menu.Foreground = Ui.Brush("TextBrush", "#F3F7F4");
            menu.BorderBrush = Ui.Brush("BorderBrush", "#2D4634");
            AddMenuItem(menu, "NVIDIA Control Panel", delegate { NvDisplayEngine.OpenNvidiaControlPanel(); });
            AddMenuItem(menu, "Windows Display Settings", delegate { try { Process.Start("ms-settings:display"); } catch { } });
            AddMenuItem(menu, "Diagnostics", delegate { OpenDiagnostics(); });
            AddMenuItem(menu, "Open data folder", delegate { try { Process.Start("explorer.exe", AppPaths.DataDir); } catch { } });
            AddMenuItem(menu, "Open NVAPI log", delegate { OpenLog(); });
            anchor.ContextMenu = menu;
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void CaptureProfile()
        {
            if (busy) return;
            NameDialog dialog = new NameDialog("Capture Profile", "Name this display layout", "This captures the exact NVIDIA topology that is active right now.", "");
            dialog.Owner = this;
            bool? result = dialog.ShowDialog();
            if (result != true) return;
            string name = dialog.Value;
            SetBusy(true, "Capturing current topology…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                ProfileRecord record = ProfileStore.CaptureNew(name, out error);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SetBusy(false, record == null ? "Capture failed" : "Captured “" + record.Name + "”");
                    if (record == null) ShowError("Capture failed", error);
                    else RefreshProfiles();
                });
            });
        }

        private void ImportProfile()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Import DisplayDeck profile";
            dialog.Filter = "DisplayDeck profile (*.displaydeck.json;*.displaymode.json;*.nvprofile.json;*.json)|*.displaydeck.json;*.displaymode.json;*.nvprofile.json;*.json|All files (*.*)|*.*";
            if (dialog.ShowDialog(this) != true) return;
            string error;
            ProfileRecord record = ProfileStore.Import(dialog.FileName, out error);
            if (record == null) ShowError("Import failed", error);
            else { RefreshProfiles(); SetStatus("Imported “" + record.Name + "”", true); }
        }

        private void UpdateProfile(ProfileRecord profile)
        {
            if (System.Windows.MessageBox.Show(this, "Replace “" + profile.Name + "” with the display topology that is active right now?\n\nA backup of the old version will be kept.", "Update profile", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            SetBusy(true, "Updating “" + profile.Name + "”…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = ProfileStore.UpdateFromCurrent(profile.Id, out error);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SetBusy(false, ok ? "Updated “" + profile.Name + "”" : "Update failed");
                    if (!ok) ShowError("Update failed", error);
                    else RefreshProfiles();
                });
            });
        }

        private void RenameProfile(ProfileRecord profile)
        {
            NameDialog dialog = new NameDialog("Rename Profile", "Rename this profile", "Give this display layout a clear name.", profile.Name);
            dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;
            string error;
            if (!ProfileStore.Rename(profile.Id, dialog.Value, out error)) ShowError("Rename failed", error);
            else RefreshProfiles();
        }

        private void DuplicateProfile(ProfileRecord profile)
        {
            string error;
            ProfileRecord copy = ProfileStore.Duplicate(profile.Id, out error);
            if (copy == null) ShowError("Duplicate failed", error);
            else { RefreshProfiles(); SetStatus("Created “" + copy.Name + "”", true); }
        }

        private void DeleteProfile(ProfileRecord profile)
        {
            if (System.Windows.MessageBox.Show(this, "Delete “" + profile.Name + "”?\n\nA backup will be kept in the profile backups folder.", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string error;
            if (!ProfileStore.Delete(profile.Id, out error)) ShowError("Delete failed", error);
            else
            {
                if (currentProfileId == profile.Id) { currentProfileId = ""; WriteCurrentProfile(""); }
                if (settings.StartupProfileId == profile.Id) { settings.StartupProfileId = ""; SettingsStore.Save(settings); }
                RefreshProfiles();
                SetStatus("Deleted “" + profile.Name + "”", true);
            }
        }

        private void AssignHotkey(ProfileRecord profile)
        {
            HotkeyDialog dialog = new HotkeyDialog(profile.HotkeyKey, profile.HotkeyModifiers);
            dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;
            ProfileStore.SetHotkey(profile.Id, dialog.HotkeyKey, dialog.HotkeyModifiers);
            RefreshProfiles();
            SetStatus(dialog.HotkeyKey == 0 ? "Hotkey cleared" : "Hotkey set to " + HotkeyHelper.Format(dialog.HotkeyKey, dialog.HotkeyModifiers), true);
        }

        private void ToggleStartupProfile(ProfileRecord profile)
        {
            settings.StartupProfileId = settings.StartupProfileId == profile.Id ? "" : profile.Id;
            SettingsStore.Save(settings);
            RefreshProfiles();
            SetStatus(string.IsNullOrWhiteSpace(settings.StartupProfileId) ? "Startup profile cleared" : profile.Name + " will load at startup", true);
        }

        private void ExportProfile(ProfileRecord profile)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "Export DisplayDeck profile";
            dialog.Filter = "DisplayDeck profile (*.displaydeck.json)|*.displaydeck.json";
            dialog.FileName = SafeFileName(profile.Name) + ".displaydeck.json";
            if (dialog.ShowDialog(this) != true) return;
            string error;
            if (!ProfileStore.Export(profile.Id, dialog.FileName, out error)) ShowError("Export failed", error);
            else SetStatus("Exported “" + profile.Name + "”", true);
        }
    }
}
