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
    internal sealed class HotkeyDialog : DialogBase
    {
        private int key;
        private int modifiers;
        private TextBlock display;
        public int HotkeyKey { get { return key; } }
        public int HotkeyModifiers { get { return modifiers; } }

        public HotkeyDialog(int currentKey, int currentModifiers) : base("Assign Hotkey", 540)
        {
            key = currentKey;
            modifiers = currentModifiers;
            AddHeading("Assign a global shortcut", "Press the key combination you want to use for this profile. Use at least one modifier such as Ctrl, Alt, Shift, or Win.");

            Border recorder = new Border();
            recorder.Background = Ui.Brush("InputBrush", "#0E1611");
            recorder.BorderBrush = Ui.Brush("BorderStrongBrush", "#41654A");
            recorder.BorderThickness = new Thickness(1);
            recorder.CornerRadius = new CornerRadius(12);
            recorder.Padding = new Thickness(18, 22, 18, 22);
            display = Ui.Text(HotkeyHelper.Format(key, modifiers), 20, Ui.Brush("AccentBrush", "#76B900"), FontWeights.SemiBold);
            display.HorizontalAlignment = HorizontalAlignment.Center;
            recorder.Child = display;
            Body.Children.Add(recorder);
            TextBlock hint = Ui.Text("Click this window and press your shortcut now.", 12, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
            hint.Margin = new Thickness(0, 10, 0, 0);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            Body.Children.Add(hint);

            Button clear = FooterButton("Clear", false);
            clear.Click += delegate { key = 0; modifiers = 0; display.Text = "No hotkey"; };
            Button cancel = FooterButton("Cancel", false);
            cancel.Click += delegate { DialogResult = false; };
            Button save = FooterButton("Save", true);
            save.Click += delegate { DialogResult = true; };

            PreviewKeyDown += CaptureKey;
        }

        private void CaptureKey(object sender, KeyEventArgs e)
        {
            Key pressed = e.Key == Key.System ? e.SystemKey : e.Key;
            if (pressed == Key.LeftCtrl || pressed == Key.RightCtrl || pressed == Key.LeftAlt || pressed == Key.RightAlt || pressed == Key.LeftShift || pressed == Key.RightShift || pressed == Key.LWin || pressed == Key.RWin) return;
            ModifierKeys current = Keyboard.Modifiers;
            int mods = 0;
            if ((current & ModifierKeys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
            if ((current & ModifierKeys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
            if ((current & ModifierKeys.Shift) != 0) mods |= NativeMethods.MOD_SHIFT;
            if ((current & ModifierKeys.Windows) != 0) mods |= NativeMethods.MOD_WIN;
            if (mods == 0) return;
            key = KeyInterop.VirtualKeyFromKey(pressed);
            modifiers = mods;
            display.Text = HotkeyHelper.Format(key, modifiers);
            e.Handled = true;
        }
    }
}
