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

[assembly: AssemblyTitle("DisplayDeck")]
[assembly: AssemblyProduct("DisplayDeck")]
[assembly: AssemblyDescription("Fast NVIDIA display profile switching for Windows.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace DisplayDeckApp
{
    internal static class Program
    {
        private const string MutexName = "Local\\DisplayDeck_Mutex_8A97E0B6";
        private const string EventName = "Local\\DisplayDeck_Show_8A97E0B6";

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (EventWaitHandle existingEvent = EventWaitHandle.OpenExisting(EventName)) existingEvent.Set();
                    }
                    catch { }
                    return;
                }

                bool startup = false;
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase)) startup = true;
                }

                AppPaths.EnsureFolders();
                ProfileStore.Initialize();

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                LoadTheme(app);

                using (EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName))
                {
                    MainWindow window = new MainWindow(startup, showEvent);
                    app.Run(window);
                }
            }
        }

        private static void LoadTheme(Application app)
        {
            try
            {
                string themePath = Path.Combine(AppPaths.BaseDir, "Theme.xaml");
                if (!File.Exists(themePath)) return;
                using (FileStream fs = File.OpenRead(themePath))
                using (System.Xml.XmlReader xr = System.Xml.XmlReader.Create(fs))
                {
                    ResourceDictionary dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(xr);
                    app.Resources.MergedDictionaries.Add(dictionary);
                }
            }
            catch { }
        }
    }

    internal static class AppPaths
    {
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayDeck");
        public static readonly string ProfilesDir = Path.Combine(DataDir, "profiles");
        public static readonly string BackupsDir = Path.Combine(ProfilesDir, "backups");
        public static readonly string TempDir = Path.Combine(DataDir, "temp");
        public static readonly string EngineExe = Path.Combine(BaseDir, "NvDisplayEngine.exe");
        public static readonly string CatalogFile = Path.Combine(ProfilesDir, "catalog.json");
        public static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
        public static readonly string StateFile = Path.Combine(DataDir, "current-profile.txt");
        public static readonly string SwitchLogFile = Path.Combine(DataDir, "nvapi-switch-log.txt");
        public static readonly string IconFile = Path.Combine(BaseDir, "DisplayDeck.ico");
        public static readonly string ExePath = Assembly.GetExecutingAssembly().Location;

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(ProfilesDir);
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(TempDir);
        }
    }

    internal static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_WIN = 0x0008;
        public const int MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    internal sealed class AppSettings
    {
        public bool CloseToTray { get; set; }
        public bool ConfirmEverySwitch { get; set; }
        public int AutoRevertSeconds { get; set; }
        public string StartupProfileId { get; set; }

        public AppSettings()
        {
            CloseToTray = true;
            ConfirmEverySwitch = false;
            AutoRevertSeconds = 15;
            StartupProfileId = "";
        }
    }

    internal sealed class ProfileCatalog
    {
        public string Format { get; set; }
        public List<ProfileRecord> Profiles { get; set; }
        public ProfileCatalog() { Format = "display-modes-catalog-v2"; Profiles = new List<ProfileRecord>(); }
    }

    internal sealed class ProfileRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public bool Favorite { get; set; }
        public int SortOrder { get; set; }
        public int HotkeyKey { get; set; }
        public int HotkeyModifiers { get; set; }
        public List<MonitorSummary> Monitors { get; set; }
        public ProfileRecord() { Monitors = new List<MonitorSummary>(); }
        public override string ToString() { return Name ?? "Profile"; }
    }

    internal sealed class MonitorSummary
    {
        public uint DisplayId { get; set; }
        public string Label { get; set; }
        public string DeviceName { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Primary { get; set; }
    }

    internal sealed class NvProfile
    {
        public string format { get; set; }
        public string createdAt { get; set; }
        public List<NvProfilePath> paths { get; set; }
        public NvProfile() { paths = new List<NvProfilePath>(); }
    }

    internal sealed class NvProfilePath
    {
        public NvProfileSource sourceMode { get; set; }
        public List<NvProfileTarget> targets { get; set; }
        public NvProfilePath() { targets = new List<NvProfileTarget>(); }
    }

    internal sealed class NvProfileSource
    {
        public uint width { get; set; }
        public uint height { get; set; }
        public uint colorDepth { get; set; }
        public uint colorFormat { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public uint spanning { get; set; }
        public uint flags { get; set; }
    }

    internal sealed class NvProfileTarget
    {
        public uint displayId { get; set; }
        public uint targetId { get; set; }
        public object details { get; set; }
    }

    internal sealed class ProfileExportBundle
    {
        public string Format { get; set; }
        public string ExportedAt { get; set; }
        public ProfileRecord Profile { get; set; }
        public NvProfile Topology { get; set; }
        public ProfileExportBundle() { Format = "display-modes-export-v1"; }
    }

    internal sealed class WindowsDisplayInfo
    {
        public string DeviceName { get; set; }
        public string FriendlyName { get; set; }
        public System.Drawing.Rectangle Bounds { get; set; }
        public bool Primary { get; set; }
    }

    internal static class JsonUtil
    {
        private static readonly JavaScriptSerializer Serializer = Create();
        private static JavaScriptSerializer Create()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 16 * 1024 * 1024;
            serializer.RecursionLimit = 100;
            return serializer;
        }
        public static T Read<T>(string path) { return Serializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static T Parse<T>(string text) { return Serializer.Deserialize<T>(text); }
        public static string Stringify(object value) { return Serializer.Serialize(value); }
        public static void WriteAtomic(string path, object value)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, Serializer.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }

    internal static class SettingsStore
    {
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    AppSettings settings = JsonUtil.Read<AppSettings>(AppPaths.SettingsFile);
                    if (settings != null)
                    {
                        if (settings.AutoRevertSeconds < 5 || settings.AutoRevertSeconds > 60) settings.AutoRevertSeconds = 15;
                        if (settings.StartupProfileId == null) settings.StartupProfileId = "";
                        return settings;
                    }
                }
            }
            catch { }
            return new AppSettings();
        }
        public static void Save(AppSettings settings) { try { JsonUtil.WriteAtomic(AppPaths.SettingsFile, settings); } catch { } }
        public static bool IsStartWithWindowsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", false))
                    return key != null && key.GetValue("DisplayDeck") != null;
            }
            catch { return false; }
        }
        public static void SetStartWithWindows(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue("DisplayDeck", "\"" + AppPaths.ExePath + "\" --startup");
                    else key.DeleteValue("DisplayDeck", false);
                }
            }
            catch { }
        }
    }
}
