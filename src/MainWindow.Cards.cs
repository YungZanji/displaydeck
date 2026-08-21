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
        private UIElement BuildProfileCard(ProfileRecord profile)
        {
            bool active = string.Equals(currentProfileId, profile.Id, StringComparison.OrdinalIgnoreCase);
            Border card = Ui.CardBorder(active);

            Grid outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock name = Ui.Text(profile.Name, 22, Ui.Brush("TextBrush", "#F3F7F4"), FontWeights.SemiBold);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = compactMode ? 330 : 520;
            titleStack.Children.Add(name);
            if (profile.Favorite)
            {
                TextBlock star = Ui.Text("★", 16, Ui.Brush("AccentBrush", "#76B900"), FontWeights.Bold);
                star.Margin = new Thickness(10, 2, 0, 0);
                titleStack.Children.Add(star);
            }
            if (active)
            {
                Border activePill = new Border();
                activePill.Background = Ui.Brush("AccentSoftBrush", "#20330D");
                activePill.BorderBrush = Ui.Brush("AccentBrush", "#76B900");
                activePill.BorderThickness = new Thickness(1);
                activePill.CornerRadius = new CornerRadius(9);
                activePill.Padding = new Thickness(8, 3, 8, 3);
                activePill.Margin = new Thickness(12, 1, 0, 0);
                activePill.Child = Ui.Text("ACTIVE", 10, Ui.Brush("AccentBrush", "#76B900"), FontWeights.Bold);
                titleStack.Children.Add(activePill);
            }
            Grid.SetColumn(titleStack, 0);
            top.Children.Add(titleStack);

            Button menuButton = Ui.Button("•••", false);
            menuButton.Style = Ui.Style("GhostButton");
            menuButton.MinWidth = 42;
            menuButton.Padding = new Thickness(10, 6, 10, 6);
            menuButton.HorizontalAlignment = HorizontalAlignment.Right;
            menuButton.Click += delegate { OpenProfileMenu(menuButton, profile); };
            Grid.SetColumn(menuButton, 1);
            top.Children.Add(menuButton);
            Grid.SetRow(top, 0);
            outer.Children.Add(top);

            Grid body = new Grid();
            body.Margin = new Thickness(0, 18, 0, 0);
            double previewColumn = compactMode ? 145 : 220;
            double gapColumn = compactMode ? 16 : 26;
            double buttonColumn = compactMode ? 108 : 132;
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(previewColumn) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gapColumn) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compactMode ? 14 : 24) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(buttonColumn) });

            Border previewBorder = new Border();
            previewBorder.Background = Ui.Brush("InputBrush", "#0E1611");
            previewBorder.BorderBrush = Ui.Brush("BorderBrush", "#2D4634");
            previewBorder.BorderThickness = new Thickness(1);
            previewBorder.CornerRadius = new CornerRadius(12);
            previewBorder.Padding = new Thickness(10);
            previewBorder.Height = compactMode ? 104 : 132;
            previewBorder.Child = BuildTopologyPreview(profile, compactMode ? 123 : 198, compactMode ? 82 : 110);
            Grid.SetColumn(previewBorder, 0);
            body.Children.Add(previewBorder);

            StackPanel details = new StackPanel();
            details.VerticalAlignment = VerticalAlignment.Center;
            int count = profile.Monitors == null ? 0 : profile.Monitors.Count;
            MonitorSummary primary = null;
            if (profile.Monitors != null)
                foreach (MonitorSummary monitor in profile.Monitors) if (monitor.Primary) { primary = monitor; break; }
            string countText = count == 1 ? "1 display" : count + " displays";
            string primaryText = primary == null ? "Primary not identified" : "Primary " + FriendlyMonitorName(primary, 0);
            TextBlock topology = Ui.Text(countText + "  •  " + primaryText, 14, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
            topology.Margin = new Thickness(0, 0, 0, 9);
            TextBlock hotkey = Ui.Text(HotkeyHelper.Format(profile.HotkeyKey, profile.HotkeyModifiers), 14, profile.HotkeyKey == 0 ? Ui.Brush("MutedBrush", "#9FB1A4") : Ui.Brush("AccentBrush", "#76B900"), FontWeights.SemiBold);
            hotkey.Margin = new Thickness(0, 0, 0, 8);
            string updated = "Updated " + FriendlyDate(profile.UpdatedAt);
            TextBlock meta = Ui.Text(updated, 12, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
            details.Children.Add(topology);
            details.Children.Add(hotkey);
            details.Children.Add(meta);
            Grid.SetColumn(details, 2);
            body.Children.Add(details);

            StackPanel buttons = new StackPanel();
            buttons.VerticalAlignment = VerticalAlignment.Center;
            Button activate = Ui.Button("Activate", true);
            activate.HorizontalAlignment = HorizontalAlignment.Stretch;
            activate.Click += delegate { ActivateProfile(profile, false, false); };
            Button test = Ui.Button("Test safely", false);
            test.HorizontalAlignment = HorizontalAlignment.Stretch;
            test.Margin = new Thickness(0, 9, 0, 0);
            test.Click += delegate { ActivateProfile(profile, true, false); };
            buttons.Children.Add(activate);
            buttons.Children.Add(test);
            Grid.SetColumn(buttons, 4);
            body.Children.Add(buttons);

            Grid.SetRow(body, 1);
            outer.Children.Add(body);
            card.Child = outer;
            return card;
        }
    }
}
