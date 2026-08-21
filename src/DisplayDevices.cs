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
    internal static class DisplayDeviceHelper
    {
        public static List<WindowsDisplayInfo> GetActiveDisplays()
        {
            List<WindowsDisplayInfo> result = new List<WindowsDisplayInfo>();
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                string friendly = screen.DeviceName.Replace("\\\\.\\", "");
                result.Add(new WindowsDisplayInfo
                {
                    DeviceName = screen.DeviceName,
                    FriendlyName = friendly,
                    Bounds = screen.Bounds,
                    Primary = screen.Primary
                });
            }
            return result;
        }

        public static string DiagnosticsText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Active Windows displays:");
            foreach (WindowsDisplayInfo display in GetActiveDisplays())
            {
                sb.Append("  ").Append(display.FriendlyName).Append("  ")
                  .Append(display.Bounds.Width).Append("x").Append(display.Bounds.Height)
                  .Append(" @ ").Append(display.Bounds.X).Append(",").Append(display.Bounds.Y);
                if (display.Primary) sb.Append("  PRIMARY");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
