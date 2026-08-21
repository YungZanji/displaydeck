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
        private static readonly object Sync = new object();
        private static ProfileCatalog catalog;

        public static void Initialize()
        {
            lock (Sync)
            {
                AppPaths.EnsureFolders();
                catalog = LoadCatalogInternal();
                NormalizeOrders(catalog);
                SaveCatalogInternal(catalog);
            }
        }

        public static ProfileRecord Find(string id)
        {
            lock (Sync)
            {
                if (catalog == null) Initialize();
                foreach (ProfileRecord profile in catalog.Profiles)
                    if (string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase)) return CloneRecord(profile);
                return null;
            }
        }

        public static List<ProfileRecord> GetOrderedProfiles()
        {
            lock (Sync)
            {
                if (catalog == null) Initialize();
                List<ProfileRecord> ordered = Ordered(catalog.Profiles, true);
                List<ProfileRecord> result = new List<ProfileRecord>();
                foreach (ProfileRecord profile in ordered) result.Add(CloneRecord(profile));
                return result;
            }
        }

        public static string GetProfilePath(ProfileRecord profile) { return Path.Combine(AppPaths.ProfilesDir, profile.FileName); }
        public static NvProfile ReadTopology(ProfileRecord profile) { return JsonUtil.Read<NvProfile>(GetProfilePath(profile)); }

        public static ProfileRecord CaptureNew(string requestedName, out string error)
        {
            error = null;
            lock (Sync)
            {
                if (catalog == null) Initialize();
                string id = Guid.NewGuid().ToString("N");
                string fileName = id + ".nvprofile.json";
                string path = Path.Combine(AppPaths.ProfilesDir, fileName);
                if (!NvDisplayEngine.Capture(path, out error)) return null;

                ProfileRecord record = new ProfileRecord();
                record.Id = id;
                record.Name = UniqueName(catalog, string.IsNullOrWhiteSpace(requestedName) ? "New Profile" : requestedName.Trim(), null);
                record.FileName = fileName;
                record.CreatedAt = DateTime.Now.ToString("o");
                record.UpdatedAt = record.CreatedAt;
                record.SortOrder = NextOrder(catalog);
                record.Monitors = BuildMonitorSummaries(path);
                catalog.Profiles.Add(record);
                SaveCatalogInternal(catalog);
                return CloneRecord(record);
            }
        }

        public static bool UpdateFromCurrent(string id, out string error)
        {
            error = null;
            lock (Sync)
            {
                ProfileRecord record = FindInternal(id);
                if (record == null) { error = "Profile not found."; return false; }
                BackupInternal(record, "before-update");
                string path = GetProfilePath(record);
                if (!NvDisplayEngine.Capture(path, out error)) return false;
                record.UpdatedAt = DateTime.Now.ToString("o");
                record.Monitors = BuildMonitorSummaries(path);
                SaveCatalogInternal(catalog);
                return true;
            }
        }

        public static bool Rename(string id, string name, out string error)
        {
            error = null;
            lock (Sync)
            {
                ProfileRecord record = FindInternal(id);
                if (record == null) { error = "Profile not found."; return false; }
                if (string.IsNullOrWhiteSpace(name)) { error = "Profile name cannot be empty."; return false; }
                BackupInternal(record, "before-rename");
                record.Name = UniqueName(catalog, name.Trim(), id);
                record.UpdatedAt = DateTime.Now.ToString("o");
                SaveCatalogInternal(catalog);
                return true;
            }
        }

        public static ProfileRecord Duplicate(string id, out string error)
        {
            error = null;
            lock (Sync)
            {
                ProfileRecord source = FindInternal(id);
                if (source == null) { error = "Profile not found."; return null; }
                string newId = Guid.NewGuid().ToString("N");
                string newFile = newId + ".nvprofile.json";
                try { File.Copy(GetProfilePath(source), Path.Combine(AppPaths.ProfilesDir, newFile), false); }
                catch (Exception ex) { error = ex.Message; return null; }

                ProfileRecord copy = CloneRecord(source);
                copy.Id = newId;
                copy.FileName = newFile;
                copy.Name = UniqueName(catalog, source.Name + " Copy", null);
                copy.CreatedAt = DateTime.Now.ToString("o");
                copy.UpdatedAt = copy.CreatedAt;
                copy.SortOrder = NextOrder(catalog);
                copy.HotkeyKey = 0;
                copy.HotkeyModifiers = 0;
                catalog.Profiles.Add(copy);
                SaveCatalogInternal(catalog);
                return CloneRecord(copy);
            }
        }

        public static bool Delete(string id, out string error)
        {
            error = null;
            lock (Sync)
            {
                ProfileRecord record = FindInternal(id);
                if (record == null) return true;
                try
                {
                    BackupInternal(record, "deleted");
                    string path = GetProfilePath(record);
                    if (File.Exists(path)) File.Delete(path);
                    catalog.Profiles.Remove(record);
                    NormalizeOrders(catalog);
                    SaveCatalogInternal(catalog);
                    return true;
                }
                catch (Exception ex) { error = ex.Message; return false; }
            }
        }

        public static void SetFavorite(string id, bool favorite)
        {
            lock (Sync)
            {
                ProfileRecord record = FindInternal(id);
                if (record == null) return;
                record.Favorite = favorite;
                SaveCatalogInternal(catalog);
            }
        }

        public static void SetHotkey(string id, int key, int modifiers)
        {
            lock (Sync)
            {
                ProfileRecord record = FindInternal(id);
                if (record == null) return;
                record.HotkeyKey = key;
                record.HotkeyModifiers = modifiers;
                SaveCatalogInternal(catalog);
            }
        }

        public static void Move(string id, int delta)
        {
            lock (Sync)
            {
                List<ProfileRecord> ordered = Ordered(catalog.Profiles, false);
                int index = -1;
                for (int i = 0; i < ordered.Count; i++) if (ordered[i].Id == id) { index = i; break; }
                if (index < 0) return;
                int next = index + delta;
                if (next < 0 || next >= ordered.Count) return;
                int temp = ordered[index].SortOrder;
                ordered[index].SortOrder = ordered[next].SortOrder;
                ordered[next].SortOrder = temp;
                SaveCatalogInternal(catalog);
            }
        }

        public static bool Export(string id, string destination, out string error)
        {
            error = null;
            lock (Sync)
            {
                ProfileRecord record = FindInternal(id);
                if (record == null) { error = "Profile not found."; return false; }
                try
                {
                    ProfileExportBundle bundle = new ProfileExportBundle();
                    bundle.ExportedAt = DateTime.Now.ToString("o");
                    bundle.Profile = CloneRecord(record);
                    bundle.Topology = ReadTopology(record);
                    JsonUtil.WriteAtomic(destination, bundle);
                    return true;
                }
                catch (Exception ex) { error = ex.Message; return false; }
            }
        }

        public static ProfileRecord Import(string sourceFile, out string error)
        {
            error = null;
            lock (Sync)
            {
                try
                {
                    string text = File.ReadAllText(sourceFile, Encoding.UTF8);
                    ProfileExportBundle bundle = null;
                    try { bundle = JsonUtil.Parse<ProfileExportBundle>(text); } catch { }
                    NvProfile topology = null;
                    ProfileRecord sourceRecord = null;
                    string requestedName = Path.GetFileNameWithoutExtension(sourceFile);

                    if (bundle != null && string.Equals(bundle.Format, "display-modes-export-v1", StringComparison.OrdinalIgnoreCase) && bundle.Topology != null)
                    {
                        topology = bundle.Topology;
                        sourceRecord = bundle.Profile;
                        if (sourceRecord != null && !string.IsNullOrWhiteSpace(sourceRecord.Name)) requestedName = sourceRecord.Name;
                    }
                    else
                    {
                        topology = JsonUtil.Parse<NvProfile>(text);
                    }

                    if (topology == null || topology.paths == null || topology.paths.Count == 0 || !string.Equals(topology.format, "display-modes-nvapi-v1", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "That file is not a compatible DisplayDeck / NVAPI profile.";
                        return null;
                    }

                    string id = Guid.NewGuid().ToString("N");
                    string fileName = id + ".nvprofile.json";
                    string destination = Path.Combine(AppPaths.ProfilesDir, fileName);
                    JsonUtil.WriteAtomic(destination, topology);

                    ProfileRecord record = new ProfileRecord();
                    record.Id = id;
                    record.Name = UniqueName(catalog, requestedName, null);
                    record.FileName = fileName;
                    record.CreatedAt = DateTime.Now.ToString("o");
                    record.UpdatedAt = record.CreatedAt;
                    record.SortOrder = NextOrder(catalog);
                    record.Favorite = sourceRecord != null && sourceRecord.Favorite;
                    record.Monitors = sourceRecord != null && sourceRecord.Monitors != null && sourceRecord.Monitors.Count > 0 ? CloneMonitors(sourceRecord.Monitors) : BuildMonitorSummaries(destination);
                    catalog.Profiles.Add(record);
                    SaveCatalogInternal(catalog);
                    return CloneRecord(record);
                }
                catch (Exception ex) { error = ex.Message; return null; }
            }
        }
    }
}
