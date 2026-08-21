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
    internal sealed class NameDialog : DialogBase
    {
        private readonly TextBox input;
        public string Value { get { return input.Text.Trim(); } }

        public NameDialog(string windowTitle, string heading, string description, string initial) : base(windowTitle, 540)
        {
            AddHeading(heading, description);
            TextBlock label = Ui.Text("PROFILE NAME", 11, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.SemiBold);
            label.Margin = new Thickness(0, 0, 0, 7);
            Body.Children.Add(label);
            input = new TextBox();
            input.Text = initial ?? "";
            input.MinHeight = 42;
            input.SelectAll();
            Body.Children.Add(input);

            Button cancel = FooterButton("Cancel", false);
            cancel.Click += delegate { DialogResult = false; };
            Button save = FooterButton(windowTitle.StartsWith("Capture") ? "Capture" : "Save", true);
            save.Click += delegate
            {
                if (string.IsNullOrWhiteSpace(input.Text))
                {
                    System.Windows.MessageBox.Show(this, "Enter a profile name first.", windowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                DialogResult = true;
            };
            Loaded += delegate { input.Focus(); };
            input.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; } };
        }
    }
}
