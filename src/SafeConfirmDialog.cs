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
    internal sealed class SafeConfirmDialog : DialogBase
    {
        private int remaining;
        private readonly TextBlock countdown;
        private readonly DispatcherTimer timer;

        public SafeConfirmDialog(string profileName, int seconds) : base("Confirm Display Configuration", 540)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            remaining = Math.Max(5, seconds);
            AddHeading("Keep “" + profileName + "”?", "If this display setup is wrong or inaccessible, DisplayDeck will automatically restore the previous topology.");

            Border counter = new Border();
            counter.Background = Ui.Brush("AccentSoftBrush", "#20330D");
            counter.BorderBrush = Ui.Brush("AccentBrush", "#76B900");
            counter.BorderThickness = new Thickness(1);
            counter.CornerRadius = new CornerRadius(14);
            counter.Padding = new Thickness(18, 20, 18, 20);
            countdown = Ui.Text("Reverting in " + remaining + " seconds", 21, Ui.Brush("AccentBrush", "#76B900"), FontWeights.SemiBold);
            countdown.HorizontalAlignment = HorizontalAlignment.Center;
            counter.Child = countdown;
            Body.Children.Add(counter);

            Button revert = FooterButton("Revert Now", false);
            revert.Click += delegate { StopTimer(); DialogResult = false; };
            Button keep = FooterButton("Keep Configuration", true);
            keep.Click += delegate { StopTimer(); DialogResult = true; };

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += delegate
            {
                remaining--;
                if (remaining <= 0)
                {
                    StopTimer();
                    DialogResult = false;
                    return;
                }
                countdown.Text = "Reverting in " + remaining + " seconds";
            };
            Loaded += delegate { timer.Start(); };
            Closed += delegate { StopTimer(); };
        }

        private void StopTimer() { try { timer.Stop(); } catch { } }
    }
}
