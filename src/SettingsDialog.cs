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
    internal sealed class SettingsDialog : DialogBase
    {
        private readonly CheckBox startWithWindows;
        private readonly CheckBox closeToTray;
        private readonly CheckBox confirmSwitch;
        private readonly TextBox seconds;
        private readonly ComboBox startup;
        private readonly List<ProfileRecord> profiles;
        public AppSettings Settings { get; private set; }
        public bool StartWithWindows { get; private set; }

        public SettingsDialog(AppSettings source, List<ProfileRecord> profileList) : base("Settings", 590)
        {
            profiles = profileList;
            Settings = new AppSettings();
            Settings.CloseToTray = source.CloseToTray;
            Settings.ConfirmEverySwitch = source.ConfirmEverySwitch;
            Settings.AutoRevertSeconds = source.AutoRevertSeconds;
            Settings.StartupProfileId = source.StartupProfileId;

            AddHeading("Settings", "Choose how DisplayDeck starts, switches profiles, and behaves when you close the window.");
            startWithWindows = new CheckBox { Content = "Start DisplayDeck with Windows", IsChecked = SettingsStore.IsStartWithWindowsEnabled() };
            closeToTray = new CheckBox { Content = "Close the window to the notification tray", IsChecked = Settings.CloseToTray };
            confirmSwitch = new CheckBox { Content = "Use safe confirmation for every profile switch", IsChecked = Settings.ConfirmEverySwitch };
            Body.Children.Add(startWithWindows);
            Body.Children.Add(closeToTray);
            Body.Children.Add(confirmSwitch);

            TextBlock secondsLabel = Ui.Text("AUTO-REVERT COUNTDOWN (5–60 SECONDS)", 11, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.SemiBold);
            secondsLabel.Margin = new Thickness(0, 18, 0, 7);
            Body.Children.Add(secondsLabel);
            seconds = new TextBox { Text = Settings.AutoRevertSeconds.ToString(), Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
            Body.Children.Add(seconds);

            TextBlock startupLabel = Ui.Text("STARTUP PROFILE", 11, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.SemiBold);
            startupLabel.Margin = new Thickness(0, 20, 0, 7);
            Body.Children.Add(startupLabel);
            startup = new ComboBox();
            startup.Items.Add(new ProfileChoice("", "None — keep the current display layout"));
            int selected = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                startup.Items.Add(new ProfileChoice(profiles[i].Id, profiles[i].Name));
                if (profiles[i].Id == Settings.StartupProfileId) selected = i + 1;
            }
            startup.SelectedIndex = selected;
            startup.MaxWidth = 500;
            startup.HorizontalAlignment = HorizontalAlignment.Stretch;
            Body.Children.Add(startup);

            Button cancel = FooterButton("Cancel", false);
            cancel.Click += delegate { DialogResult = false; };
            Button save = FooterButton("Save Settings", true);
            save.Click += SaveClicked;
        }

        private void SaveClicked(object sender, RoutedEventArgs e)
        {
            int count;
            if (!int.TryParse(seconds.Text.Trim(), out count) || count < 5 || count > 60)
            {
                System.Windows.MessageBox.Show(this, "Auto-revert must be between 5 and 60 seconds.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Settings.CloseToTray = closeToTray.IsChecked == true;
            Settings.ConfirmEverySwitch = confirmSwitch.IsChecked == true;
            Settings.AutoRevertSeconds = count;
            ProfileChoice choice = startup.SelectedItem as ProfileChoice;
            Settings.StartupProfileId = choice == null ? "" : choice.Id;
            StartWithWindows = startWithWindows.IsChecked == true;
            DialogResult = true;
        }

        private sealed class ProfileChoice
        {
            public string Id { get; private set; }
            private readonly string name;
            public ProfileChoice(string id, string value) { Id = id; name = value; }
            public override string ToString() { return name; }
        }
    }
}
