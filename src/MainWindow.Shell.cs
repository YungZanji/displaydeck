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
    internal sealed partial class MainWindow : Window
    {
        private readonly bool startupLaunch;
        private readonly EventWaitHandle showEvent;
        private StackPanel profilePanel;
        private Border emptyState;
        private TextBlock statusText;
        private TextBlock profileCountText;
        private Button captureButton;
        private bool realExit;
        private bool busy;
        private bool compactMode;
        private string currentProfileId;
        private AppSettings settings;
        private System.Windows.Forms.NotifyIcon tray;
        private HwndSource hwndSource;
        private readonly Dictionary<int, string> hotkeyMap = new Dictionary<int, string>();
        private readonly List<int> registeredHotkeys = new List<int>();
        private int nextHotkeyId = 0x7100;

        public MainWindow(bool startup, EventWaitHandle evt)
        {
            startupLaunch = startup;
            showEvent = evt;
            settings = SettingsStore.Load();
            currentProfileId = ReadCurrentProfile();

            Title = "DisplayDeck";
            Width = 980;
            Height = 760;
            MinWidth = 540;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            Ui.ApplyWindowBase(this);
            try
            {
                if (File.Exists(AppPaths.IconFile)) Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(AppPaths.IconFile));
            }
            catch { }

            Content = BuildLayout();
            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            SizeChanged += delegate
            {
                ClampToWorkArea();
                bool nextCompact = ActualWidth > 0 && ActualWidth < 790;
                if (nextCompact != compactMode)
                {
                    compactMode = nextCompact;
                    RefreshProfiles();
                }
            };

            BuildTray();
            RefreshProfiles();
            StartShowListener();
        }

        private UIElement BuildLayout()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border header = new Border();
            header.Background = Ui.Brush("SurfaceBrush", "#101813");
            header.BorderBrush = Ui.Brush("BorderBrush", "#2D4634");
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.Padding = new Thickness(30, 26, 30, 22);

            StackPanel headerStack = new StackPanel();
            Grid titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel();
            TextBlock title = Ui.Text("DisplayDeck", 34, Ui.Brush("TextBrush", "#F3F7F4"), FontWeights.Bold);
            TextBlock subtitle = Ui.Text("Capture and switch complete NVIDIA display topologies", 15, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            Grid.SetColumn(titleStack, 0);
            titleRow.Children.Add(titleStack);

            Border enginePill = new Border();
            enginePill.Background = Ui.Brush("AccentSoftBrush", "#20330D");
            enginePill.BorderBrush = Ui.Brush("BorderStrongBrush", "#41654A");
            enginePill.BorderThickness = new Thickness(1);
            enginePill.CornerRadius = new CornerRadius(12);
            enginePill.Padding = new Thickness(12, 7, 12, 7);
            enginePill.VerticalAlignment = VerticalAlignment.Top;
            enginePill.Margin = new Thickness(16, 3, 0, 0);
            enginePill.Child = Ui.Text("NVAPI", 12, Ui.Brush("AccentBrush", "#76B900"), FontWeights.SemiBold);
            Grid.SetColumn(enginePill, 1);
            titleRow.Children.Add(enginePill);
            headerStack.Children.Add(titleRow);

            WrapPanel actions = new WrapPanel();
            actions.Margin = new Thickness(0, 20, 0, 0);
            captureButton = Ui.Button("+  Capture Profile", true);
            captureButton.Margin = new Thickness(0, 0, 10, 8);
            captureButton.Click += delegate { CaptureProfile(); };
            Button importButton = Ui.Button("Import", false);
            importButton.Margin = new Thickness(0, 0, 10, 8);
            importButton.Click += delegate { ImportProfile(); };
            Button settingsButton = Ui.Button("Settings", false);
            settingsButton.Margin = new Thickness(0, 0, 10, 8);
            settingsButton.Click += delegate { OpenSettings(); };
            Button toolsButton = Ui.Button("Tools", false);
            toolsButton.Margin = new Thickness(0, 0, 10, 8);
            toolsButton.Click += ToolsButton_Click;
            actions.Children.Add(captureButton);
            actions.Children.Add(importButton);
            actions.Children.Add(settingsButton);
            actions.Children.Add(toolsButton);
            headerStack.Children.Add(actions);
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Padding = new Thickness(28, 24, 28, 24);
            StackPanel contentStack = new StackPanel();

            Grid sectionHeader = new Grid();
            sectionHeader.Margin = new Thickness(0, 0, 0, 16);
            sectionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sectionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock sectionTitle = Ui.Text("Profiles", 18, Ui.Brush("TextBrush", "#F3F7F4"), FontWeights.SemiBold);
            profileCountText = Ui.Text("0 saved", 13, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
            profileCountText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(profileCountText, 1);
            sectionHeader.Children.Add(sectionTitle);
            sectionHeader.Children.Add(profileCountText);
            contentStack.Children.Add(sectionHeader);

            emptyState = BuildEmptyState();
            contentStack.Children.Add(emptyState);

            profilePanel = new StackPanel();
            contentStack.Children.Add(profilePanel);
            scroller.Content = contentStack;
            Grid.SetRow(scroller, 1);
            root.Children.Add(scroller);

            Border statusBar = new Border();
            statusBar.Background = Ui.Brush("SurfaceBrush", "#101813");
            statusBar.BorderBrush = Ui.Brush("BorderBrush", "#2D4634");
            statusBar.BorderThickness = new Thickness(0, 1, 0, 0);
            statusBar.Padding = new Thickness(28, 12, 28, 12);
            statusText = Ui.Text("Ready", 13, Ui.Brush("AccentBrush", "#76B900"), FontWeights.SemiBold);
            statusBar.Child = statusText;
            Grid.SetRow(statusBar, 2);
            root.Children.Add(statusBar);

            return root;
        }

        private Border BuildEmptyState()
        {
            Border border = new Border();
            border.Background = Ui.Brush("CardBrush", "#152119");
            border.BorderBrush = Ui.Brush("BorderBrush", "#2D4634");
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(18);
            border.Padding = new Thickness(34);

            StackPanel content = new StackPanel();
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            Canvas preview = BuildPlaceholderPreview();
            preview.Width = 160;
            preview.Height = 110;
            preview.HorizontalAlignment = HorizontalAlignment.Left;
            preview.Margin = new Thickness(0, 0, 0, 18);
            content.Children.Add(preview);
            TextBlock headline = Ui.Text("Create your first display profile", 24, Ui.Brush("TextBrush", "#F3F7F4"), FontWeights.SemiBold);
            TextBlock body = Ui.Text("Arrange your monitors exactly how you want them, then capture the current topology. Each profile can have its own monitor count, positions, primary display, hotkey, and startup behavior.", 14, Ui.Brush("MutedBrush", "#9FB1A4"), FontWeights.Normal);
            body.Margin = new Thickness(0, 8, 0, 16);
            Button capture = Ui.Button("Capture Current Layout", true);
            capture.Click += delegate { CaptureProfile(); };
            content.Children.Add(headline);
            content.Children.Add(body);
            content.Children.Add(capture);
            border.Child = content;
            return border;
        }

        private Canvas BuildPlaceholderPreview()
        {
            Canvas canvas = new Canvas();
            AddPlaceholderMonitor(canvas, 51, 8, 58, 36, false);
            AddPlaceholderMonitor(canvas, 16, 60, 58, 36, true);
            AddPlaceholderMonitor(canvas, 86, 60, 58, 36, false);
            return canvas;
        }

        private void AddPlaceholderMonitor(Canvas canvas, double x, double y, double width, double height, bool primary)
        {
            Border monitor = new Border();
            monitor.Width = width;
            monitor.Height = height;
            monitor.CornerRadius = new CornerRadius(6);
            monitor.Background = Ui.Brush("InputBrush", "#0E1611");
            monitor.BorderBrush = primary ? Ui.Brush("AccentBrush", "#76B900") : Ui.Brush("BorderStrongBrush", "#41654A");
            monitor.BorderThickness = new Thickness(primary ? 2 : 1);
            Canvas.SetLeft(monitor, x);
            Canvas.SetTop(monitor, y);
            canvas.Children.Add(monitor);
        }

        private void RefreshProfiles()
        {
            if (profilePanel == null) return;
            profilePanel.Children.Clear();
            List<ProfileRecord> profiles = ProfileStore.GetOrderedProfiles();
            profileCountText.Text = profiles.Count == 1 ? "1 saved" : profiles.Count + " saved";
            emptyState.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (ProfileRecord profile in profiles) profilePanel.Children.Add(BuildProfileCard(profile));
            RebuildTrayMenu();
            RegisterProfileHotkeys();
        }
    }
}
