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
    internal static partial class ProfileStore
    {
        private static ProfileCatalog LoadCatalogInternal()
        {
            try
            {
                if (File.Exists(AppPaths.CatalogFile))
                {
                    ProfileCatalog loaded = JsonUtil.Read<ProfileCatalog>(AppPaths.CatalogFile);
                    if (loaded != null && loaded.Profiles != null) return loaded;
                }
            }
            catch { }
            return new ProfileCatalog();
        }

        private static void SaveCatalogInternal(ProfileCatalog value)
        {
            try
            {
                if (File.Exists(AppPaths.CatalogFile)) File.Copy(AppPaths.CatalogFile, Path.Combine(AppPaths.BackupsDir, "catalog-latest.json"), true);
            }
            catch { }
            JsonUtil.WriteAtomic(AppPaths.CatalogFile, value);
        }

        private static List<MonitorSummary> BuildMonitorSummaries(string profilePath)
        {
            List<MonitorSummary> list = new List<MonitorSummary>();
            try
            {
                NvProfile topology = JsonUtil.Read<NvProfile>(profilePath);
                List<WindowsDisplayInfo> active = DisplayDeviceHelper.GetActiveDisplays();
                int fallback = 1;
                foreach (NvProfilePath path in topology.paths)
                {
                    if (path == null || path.sourceMode == null || path.targets == null) continue;
                    WindowsDisplayInfo match = null;
                    foreach (WindowsDisplayInfo display in active)
                    {
                        System.Drawing.Rectangle bounds = display.Bounds;
                        if (bounds.X == path.sourceMode.x && bounds.Y == path.sourceMode.y && bounds.Width == (int)path.sourceMode.width && bounds.Height == (int)path.sourceMode.height)
                        {
                            match = display;
                            break;
                        }
                    }
                    foreach (NvProfileTarget target in path.targets)
                    {
                        MonitorSummary monitor = new MonitorSummary();
                        monitor.DisplayId = target.displayId;
                        monitor.X = path.sourceMode.x;
                        monitor.Y = path.sourceMode.y;
                        monitor.Width = (int)path.sourceMode.width;
                        monitor.Height = (int)path.sourceMode.height;
                        monitor.Primary = (path.sourceMode.flags & 1) != 0 || (match != null && match.Primary);
                        monitor.DeviceName = match != null ? match.DeviceName : "";
                        monitor.Label = match != null ? match.FriendlyName : "Display " + fallback;
                        list.Add(monitor);
                        fallback++;
                    }
                }
            }
            catch { }
            return list;
        }

        private static void BackupInternal(ProfileRecord record, string reason)
        {
            try
            {
                ProfileExportBundle bundle = new ProfileExportBundle();
                bundle.ExportedAt = DateTime.Now.ToString("o");
                bundle.Profile = CloneRecord(record);
                bundle.Topology = ReadTopology(record);
                string safe = SanitizeFileName(record.Name);
                JsonUtil.WriteAtomic(Path.Combine(AppPaths.BackupsDir, safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + reason + ".displaydeck.json"), bundle);
            }
            catch { }
        }

        private static ProfileRecord FindInternal(string id)
        {
            if (catalog == null) return null;
            foreach (ProfileRecord profile in catalog.Profiles) if (profile.Id == id) return profile;
            return null;
        }

        private static string UniqueName(ProfileCatalog value, string requested, string excludeId)
        {
            string baseName = string.IsNullOrWhiteSpace(requested) ? "Profile" : requested.Trim();
            string candidate = baseName;
            int number = 2;
            while (NameExists(value, candidate, excludeId)) { candidate = baseName + " " + number; number++; }
            return candidate;
        }

        private static bool NameExists(ProfileCatalog value, string name, string excludeId)
        {
            foreach (ProfileRecord profile in value.Profiles)
            {
                if (profile.Id == excludeId) continue;
                if (string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int NextOrder(ProfileCatalog value)
        {
            int max = -1;
            foreach (ProfileRecord profile in value.Profiles) if (profile.SortOrder > max) max = profile.SortOrder;
            return max + 1;
        }

        private static void NormalizeOrders(ProfileCatalog value)
        {
            List<ProfileRecord> ordered = Ordered(value.Profiles, false);
            for (int i = 0; i < ordered.Count; i++) ordered[i].SortOrder = i;
        }

        private static List<ProfileRecord> Ordered(List<ProfileRecord> input, bool favoritesFirst)
        {
            List<ProfileRecord> result = new List<ProfileRecord>(input);
            result.Sort(delegate(ProfileRecord a, ProfileRecord b)
            {
                if (favoritesFirst && a.Favorite != b.Favorite) return a.Favorite ? -1 : 1;
                int compare = a.SortOrder.CompareTo(b.SortOrder);
                if (compare != 0) return compare;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static ProfileRecord CloneRecord(ProfileRecord profile)
        {
            if (profile == null) return null;
            ProfileRecord copy = new ProfileRecord();
            copy.Id = profile.Id;
            copy.Name = profile.Name;
            copy.FileName = profile.FileName;
            copy.CreatedAt = profile.CreatedAt;
            copy.UpdatedAt = profile.UpdatedAt;
            copy.Favorite = profile.Favorite;
            copy.SortOrder = profile.SortOrder;
            copy.HotkeyKey = profile.HotkeyKey;
            copy.HotkeyModifiers = profile.HotkeyModifiers;
            copy.Monitors = CloneMonitors(profile.Monitors);
            return copy;
        }

        private static List<MonitorSummary> CloneMonitors(List<MonitorSummary> source)
        {
            List<MonitorSummary> result = new List<MonitorSummary>();
            if (source == null) return result;
            foreach (MonitorSummary monitor in source)
            {
                result.Add(new MonitorSummary
                {
                    DisplayId = monitor.DisplayId,
                    Label = monitor.Label,
                    DeviceName = monitor.DeviceName,
                    X = monitor.X,
                    Y = monitor.Y,
                    Width = monitor.Width,
                    Height = monitor.Height,
                    Primary = monitor.Primary
                });
            }
            return result;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "profile";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            return value.Trim();
        }
    }
}
