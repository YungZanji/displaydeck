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
    internal abstract class DialogBase : Window
    {
        protected StackPanel Body;
        protected StackPanel Footer;

        protected DialogBase(string title, double width)
        {
            Title = title;
            Width = width;
            SizeToContent = SizeToContent.Height;
            MinWidth = Math.Min(460, width);
            MaxWidth = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Ui.ApplyWindowBase(this);
            SourceInitialized += delegate { Ui.ApplyDarkTitleBar(this); };

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Body = new StackPanel();
            Body.Margin = new Thickness(28, 26, 28, 24);
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Content = Body;
            Grid.SetRow(scroller, 0);
            root.Children.Add(scroller);

            Border footerBorder = new Border();
            footerBorder.BorderBrush = Ui.Brush("BorderBrush", "#2D4634");
            footerBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            footerBorder.Background = Ui.Brush("SurfaceBrush", "#101813");
            footerBorder.Padding = new Thickness(28, 16, 28, 16);
            Footer = new StackPanel();
            Footer.Orientation = Orientation.Horizontal;
            Footer.HorizontalAlignment = HorizontalAlignment.Right;
            footerBorder.Child = Footer;
            Grid.SetRow(footerBorder, 1);
            root.Children.Add(footerBorder);
            Content = root;
        }

        protected void AddHeading(string heading, string description)
        {
            TextBlock title = Ui.Text(heading, 24, Ui.Brush("TextBrush", "#F3F7F4"), FontWeights.SemiBold);
            TextBlock desc = Ui.Text(description, 14, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
            desc.Margin = new Thickness(0, 7, 0, 22);
            Body.Children.Add(title);
            Body.Children.Add(desc);
        }

        protected Button FooterButton(string text, bool primary)
        {
            Button button = Ui.Button(text, primary);
            button.Margin = new Thickness(8, 0, 0, 0);
            Footer.Children.Add(button);
            return button;
        }
    }
}
