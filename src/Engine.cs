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
    internal static class NvDisplayEngine
    {
        public static bool Capture(string profile, out string error)
        {
            string output;
            return Run("capture \"" + profile + "\"", "capture " + Path.GetFileName(profile), out output, out error);
        }

        public static bool Apply(string profile, string action, out string error)
        {
            string output;
            return Run("apply \"" + profile + "\"", action, out output, out error);
        }

        public static bool Probe(out string output, out string error) { return Run("probe", "probe", out output, out error); }

        private static bool Run(string args, string action, out string output, out string error)
        {
            output = "";
            error = null;
            AppPaths.EnsureFolders();
            if (!File.Exists(AppPaths.EngineExe)) { error = "NvDisplayEngine.exe is missing. Run Setup.cmd again."; return false; }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = AppPaths.EngineExe;
                info.Arguments = args;
                info.WorkingDirectory = AppPaths.BaseDir;
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                string stdout;
                string stderr;
                int exitCode;
                using (Process process = Process.Start(info))
                {
                    if (process == null) { error = "The NVAPI engine could not be started."; return false; }
                    stdout = process.StandardOutput.ReadToEnd();
                    stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch { }
                        error = "The NVAPI engine timed out while trying to " + action + ".";
                        Log(action, -1, stdout, stderr, error);
                        return false;
                    }
                    exitCode = process.ExitCode;
                }
                output = stdout;
                if (exitCode != 0)
                {
                    string detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                    if (string.IsNullOrWhiteSpace(detail)) detail = "NVAPI engine exit code " + exitCode + ".";
                    error = detail;
                    Log(action, exitCode, stdout, stderr, error);
                    return false;
                }
                Log(action, exitCode, stdout, stderr, null);
                return true;
            }
            catch (Exception ex)
            {
                error = "The NVAPI engine could not run: " + ex.Message;
                Log(action, -1, "", "", error);
                return false;
            }
        }

        private static void Log(string action, int exitCode, string stdout, string stderr, string error)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("  ").Append(action).Append("  exit=").Append(exitCode);
                if (!string.IsNullOrWhiteSpace(stdout)) sb.Append("  out=").Append(stdout.Trim().Replace("\r", " ").Replace("\n", " | "));
                if (!string.IsNullOrWhiteSpace(stderr)) sb.Append("  err=").Append(stderr.Trim().Replace("\r", " ").Replace("\n", " | "));
                if (!string.IsNullOrWhiteSpace(error)) sb.Append("  ERROR=").Append(error.Replace("\r", " ").Replace("\n", " | "));
                sb.AppendLine();
                File.AppendAllText(AppPaths.SwitchLogFile, sb.ToString());
            }
            catch { }
        }

        public static void OpenNvidiaControlPanel()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe")
            };
            foreach (string candidate in candidates)
            {
                try { if (File.Exists(candidate)) { Process.Start(candidate); return; } } catch { }
            }
            try { Process.Start("nvcplui.exe"); return; } catch { }
            try { Process.Start("ms-settings:display"); } catch { }
        }
    }

    internal static class HotkeyHelper
    {
        public static string Format(int key, int modifiers)
        {
            if (key == 0) return "No hotkey";
            List<string> parts = new List<string>();
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
            Key wpfKey = KeyInterop.KeyFromVirtualKey(key);
            parts.Add(wpfKey.ToString());
            return string.Join(" + ", parts.ToArray());
        }
    }

    internal static class Ui
    {
        public static Brush Brush(string key, string fallback)
        {
            object value = Application.Current == null ? null : Application.Current.TryFindResource(key);
            Brush brush = value as Brush;
            if (brush != null) return brush;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
        }

        public static Style Style(string key)
        {
            object value = Application.Current == null ? null : Application.Current.TryFindResource(key);
            return value as Style;
        }

        public static TextBlock Text(string text, double size, Brush color, FontWeight weight)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontFamily = new FontFamily("Segoe UI");
            block.FontSize = size;
            block.Foreground = color;
            block.FontWeight = weight;
            block.TextWrapping = TextWrapping.Wrap;
            block.VerticalAlignment = VerticalAlignment.Center;
            return block;
        }

        public static Button Button(string text, bool primary)
        {
            Button button = new Button();
            button.Content = text;
            button.Style = Style(primary ? "PrimaryButton" : "SecondaryButton");
            button.HorizontalAlignment = HorizontalAlignment.Left;
            return button;
        }

        public static Border CardBorder(bool active)
        {
            Border border = new Border();
            border.Background = Brush("CardBrush", "#152119");
            border.BorderBrush = active ? Brush("AccentBrush", "#76B900") : Brush("BorderBrush", "#2D4634");
            border.BorderThickness = new Thickness(active ? 2 : 1);
            border.CornerRadius = new CornerRadius(16);
            border.Padding = new Thickness(20);
            border.Margin = new Thickness(0, 0, 0, 16);
            return border;
        }

        public static void ApplyWindowBase(Window window)
        {
            window.Background = Brush("BgBrush", "#0A0F0C");
            window.Foreground = Brush("TextBrush", "#F3F7F4");
            window.FontFamily = new FontFamily("Segoe UI");
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
        }

        public static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, Marshal.SizeOf(typeof(int)));
            }
            catch { }
        }
    }
}
