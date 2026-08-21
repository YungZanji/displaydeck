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
        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            Ui.ApplyDarkTitleBar(this);
            hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (hwndSource != null) hwndSource.AddHook(WndProc);
            RegisterProfileHotkeys();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ClampToWorkArea();
            if (startupLaunch)
            {
                ProfileRecord startupProfile = string.IsNullOrWhiteSpace(settings.StartupProfileId) ? null : ProfileStore.Find(settings.StartupProfileId);
                if (startupProfile != null) ActivateProfile(startupProfile, false, true);
                Hide();
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!realExit && settings.CloseToTray)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            UnregisterAllHotkeys();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (Application.Current != null) Application.Current.Shutdown();
        }

        private void ClampToWorkArea()
        {
            Rect area = SystemParameters.WorkArea;
            MaxWidth = Math.Max(MinWidth, area.Width - 24);
            MaxHeight = Math.Max(MinHeight, area.Height - 24);
            if (Width > MaxWidth) Width = MaxWidth;
            if (Height > MaxHeight) Height = MaxHeight;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                string profileId;
                if (hotkeyMap.TryGetValue(id, out profileId))
                {
                    ProfileRecord profile = ProfileStore.Find(profileId);
                    if (profile != null) ActivateProfile(profile, false, false);
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void RegisterProfileHotkeys()
        {
            if (hwndSource == null) return;
            UnregisterAllHotkeys();
            nextHotkeyId = 0x7100;
            foreach (ProfileRecord profile in ProfileStore.GetOrderedProfiles())
            {
                if (profile.HotkeyKey == 0) continue;
                int id = nextHotkeyId++;
                bool ok = false;
                try
                {
                    ok = NativeMethods.RegisterHotKey(hwndSource.Handle, id, profile.HotkeyModifiers | NativeMethods.MOD_NOREPEAT, profile.HotkeyKey);
                }
                catch { }
                if (ok)
                {
                    registeredHotkeys.Add(id);
                    hotkeyMap[id] = profile.Id;
                }
                else
                {
                    SetStatus("Hotkey conflict: " + HotkeyHelper.Format(profile.HotkeyKey, profile.HotkeyModifiers), false);
                }
            }
        }

        private void UnregisterAllHotkeys()
        {
            if (hwndSource != null)
            {
                foreach (int id in registeredHotkeys)
                {
                    try { NativeMethods.UnregisterHotKey(hwndSource.Handle, id); } catch { }
                }
            }
            registeredHotkeys.Clear();
            hotkeyMap.Clear();
        }

        private void BuildTray()
        {
            tray = new System.Windows.Forms.NotifyIcon();
            tray.Text = "DisplayDeck";
            try
            {
                if (File.Exists(AppPaths.IconFile)) tray.Icon = new System.Drawing.Icon(AppPaths.IconFile);
                else tray.Icon = System.Drawing.SystemIcons.Application;
            }
            catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            tray.Visible = true;
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke((Action)ShowWindow); };
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (tray == null) return;
            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            List<ProfileRecord> profiles = ProfileStore.GetOrderedProfiles();
            if (profiles.Count == 0)
            {
                System.Windows.Forms.ToolStripMenuItem empty = new System.Windows.Forms.ToolStripMenuItem("No profiles saved");
                empty.Enabled = false;
                menu.Items.Add(empty);
            }
            else
            {
                foreach (ProfileRecord profile in profiles)
                {
                    ProfileRecord copy = profile;
                    string label = (copy.Id == currentProfileId ? "✓  " : "") + copy.Name;
                    if (copy.HotkeyKey != 0) label += "    " + HotkeyHelper.Format(copy.HotkeyKey, copy.HotkeyModifiers);
                    menu.Items.Add(label, null, delegate { Dispatcher.BeginInvoke((Action)delegate { ActivateProfile(copy, false, false); }); });
                }
            }
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Capture Profile", null, delegate { Dispatcher.BeginInvoke((Action)delegate { ShowWindow(); CaptureProfile(); }); });
            menu.Items.Add("Open DisplayDeck", null, delegate { Dispatcher.BeginInvoke((Action)ShowWindow); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    realExit = true;
                    Close();
                });
            });
            tray.ContextMenuStrip = menu;
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void StartShowListener()
        {
            Thread thread = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        showEvent.WaitOne();
                        Dispatcher.BeginInvoke((Action)ShowWindow);
                    }
                    catch { return; }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private static string ReadCurrentProfile()
        {
            try { return File.Exists(AppPaths.StateFile) ? File.ReadAllText(AppPaths.StateFile).Trim() : ""; }
            catch { return ""; }
        }

        private static void WriteCurrentProfile(string id)
        {
            try { File.WriteAllText(AppPaths.StateFile, id ?? ""); } catch { }
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "profile";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            return value;
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
