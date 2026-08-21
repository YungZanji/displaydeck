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
    internal sealed class DiagnosticsDialog : DialogBase
    {
        private readonly TextBox output;

        public DiagnosticsDialog(List<ProfileRecord> profiles) : base("Diagnostics", 720)
        {
            Width = 720;
            Height = 620;
            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResize;
            AddHeading("Diagnostics", "This page shows the NVAPI engine status, active Windows displays, saved profiles, and the local data location.");
            output = new TextBox();
            output.IsReadOnly = true;
            output.AcceptsReturn = true;
            output.TextWrapping = TextWrapping.Wrap;
            output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            output.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            output.FontFamily = new FontFamily("Consolas");
            output.FontSize = 12;
            output.MinHeight = 360;
            output.Text = BuildDiagnostics(profiles);
            Body.Children.Add(output);

            Button copy = FooterButton("Copy", false);
            copy.Click += delegate { try { Clipboard.SetText(output.Text); } catch { } };
            Button log = FooterButton("Open Log", false);
            log.Click += delegate
            {
                try
                {
                    if (!File.Exists(AppPaths.SwitchLogFile)) File.WriteAllText(AppPaths.SwitchLogFile, "No NVAPI operations have been logged yet.\r\n");
                    Process.Start("notepad.exe", AppPaths.SwitchLogFile);
                }
                catch { }
            };
            Button close = FooterButton("Close", true);
            close.Click += delegate { DialogResult = true; };
        }

        private static string BuildDiagnostics(List<ProfileRecord> profiles)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DisplayDeck 1.0");
            sb.AppendLine("UI: WPF / device-independent layout");
            sb.AppendLine("Engine: NVIDIA NVAPI");
            sb.AppendLine("Data: " + AppPaths.DataDir);
            sb.AppendLine("Engine executable: " + AppPaths.EngineExe);
            sb.AppendLine();

            string probeOutput, probeError;
            bool probe = NvDisplayEngine.Probe(out probeOutput, out probeError);
            sb.AppendLine("NVAPI probe: " + (probe ? "OK" : "FAILED"));
            if (!string.IsNullOrWhiteSpace(probeOutput)) sb.AppendLine(probeOutput.Trim());
            if (!string.IsNullOrWhiteSpace(probeError)) sb.AppendLine(probeError.Trim());
            sb.AppendLine();
            sb.Append(DisplayDeviceHelper.DiagnosticsText());
            sb.AppendLine();
            sb.AppendLine("Saved profiles: " + profiles.Count);
            foreach (ProfileRecord profile in profiles)
            {
                sb.Append("  • ").Append(profile.Name).Append(" — ").Append(profile.Monitors == null ? 0 : profile.Monitors.Count).Append(" displays");
                if (profile.HotkeyKey != 0) sb.Append(" — ").Append(HotkeyHelper.Format(profile.HotkeyKey, profile.HotkeyModifiers));
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
