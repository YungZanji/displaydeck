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
        private Canvas BuildTopologyPreview(ProfileRecord profile, double width, double height)
        {
            Canvas canvas = new Canvas();
            canvas.Width = width;
            canvas.Height = height;
            List<MonitorSummary> monitors = profile.Monitors == null ? new List<MonitorSummary>() : profile.Monitors;
            if (monitors.Count == 0)
            {
                TextBlock noPreview = Ui.Text("No preview", 12, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
                Canvas.SetLeft(noPreview, 64);
                Canvas.SetTop(noPreview, 43);
                canvas.Children.Add(noPreview);
                return canvas;
            }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (MonitorSummary monitor in monitors)
            {
                minX = Math.Min(minX, monitor.X);
                minY = Math.Min(minY, monitor.Y);
                maxX = Math.Max(maxX, monitor.X + Math.Max(1, monitor.Width));
                maxY = Math.Max(maxY, monitor.Y + Math.Max(1, monitor.Height));
            }
            double sourceW = Math.Max(1, maxX - minX);
            double sourceH = Math.Max(1, maxY - minY);
            double scale = Math.Min((width - 12) / sourceW, (height - 12) / sourceH);
            double usedW = sourceW * scale;
            double usedH = sourceH * scale;
            double offsetX = (width - usedW) / 2.0;
            double offsetY = (height - usedH) / 2.0;

            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorSummary monitor = monitors[i];
                double x = offsetX + (monitor.X - minX) * scale;
                double y = offsetY + (monitor.Y - minY) * scale;
                double w = Math.Max(32, monitor.Width * scale);
                double h = Math.Max(22, monitor.Height * scale);
                Border box = new Border();
                box.Width = w;
                box.Height = h;
                box.CornerRadius = new CornerRadius(5);
                box.Background = monitor.Primary ? Ui.Brush("AccentSoftBrush", "#20330D") : Ui.Brush("SurfaceBrush", "#101813");
                box.BorderBrush = monitor.Primary ? Ui.Brush("AccentBrush", "#76B900") : Ui.Brush("BorderStrongBrush", "#41654A");
                box.BorderThickness = new Thickness(monitor.Primary ? 2 : 1);
                string label = FriendlyMonitorName(monitor, i + 1);
                if (monitor.Primary) label += "  ★";
                TextBlock text = Ui.Text(label, 11, monitor.Primary ? Ui.Brush("AccentBrush", "#76B900") : Ui.Brush("TextBrush", "#F3F7F4"), FontWeights.SemiBold);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.TextAlignment = TextAlignment.Center;
                box.Child = text;
                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);
                canvas.Children.Add(box);
            }
            return canvas;
        }

        private static string FriendlyMonitorName(MonitorSummary monitor, int fallback)
        {
            if (monitor == null) return "Display";
            string device = monitor.DeviceName ?? "";
            int idx = device.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string suffix = device.Substring(idx + 7).Trim();
                if (!string.IsNullOrWhiteSpace(suffix)) return "Display " + suffix;
            }
            if (!string.IsNullOrWhiteSpace(monitor.Label))
            {
                string label = monitor.Label.Replace("\\\\.\\", "");
                if (label.Length <= 18) return label;
            }
            return fallback > 0 ? "Display " + fallback : "Display";
        }

        private static string FriendlyDate(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, out parsed)) return parsed.ToString("MMM d, yyyy h:mm tt");
            return "recently";
        }

        private void OpenProfileMenu(Button anchor, ProfileRecord profile)
        {
            ContextMenu menu = new ContextMenu();
            menu.Background = Ui.Brush("SurfaceBrush", "#101813");
            menu.Foreground = Ui.Brush("TextBrush", "#F3F7F4");
            menu.BorderBrush = Ui.Brush("BorderBrush", "#2D4634");
            menu.BorderThickness = new Thickness(1);
            AddMenuItem(menu, "Update from current displays", delegate { UpdateProfile(profile); });
            AddMenuItem(menu, "Rename", delegate { RenameProfile(profile); });
            AddMenuItem(menu, "Duplicate", delegate { DuplicateProfile(profile); });
            AddMenuItem(menu, profile.Favorite ? "Remove from favorites" : "Favorite", delegate { ProfileStore.SetFavorite(profile.Id, !profile.Favorite); RefreshProfiles(); });
            AddMenuItem(menu, "Assign hotkey", delegate { AssignHotkey(profile); });
            AddMenuItem(menu, settings.StartupProfileId == profile.Id ? "Clear startup profile" : "Set as startup profile", delegate { ToggleStartupProfile(profile); });
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Move up", delegate { ProfileStore.Move(profile.Id, -1); RefreshProfiles(); });
            AddMenuItem(menu, "Move down", delegate { ProfileStore.Move(profile.Id, 1); RefreshProfiles(); });
            AddMenuItem(menu, "Export profile…", delegate { ExportProfile(profile); });
            menu.Items.Add(new Separator());
            MenuItem delete = AddMenuItem(menu, "Delete", delegate { DeleteProfile(profile); });
            delete.Foreground = Ui.Brush("DangerBrush", "#E36F75");
            anchor.ContextMenu = menu;
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private MenuItem AddMenuItem(ContextMenu menu, string header, Action action)
        {
            MenuItem item = new MenuItem();
            item.Header = header;
            item.Click += delegate { action(); };
            menu.Items.Add(item);
            return item;
        }
    }
}
