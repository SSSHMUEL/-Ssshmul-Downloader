using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using SsshmulDownloader.ArtistTracker;
using SsshmulDownloader.Server;
using SsshmulDownloader.Storage;
using WpfApplication = System.Windows.Application;

namespace SsshmulDownloader
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        private readonly LocalHttpServer _httpServer = new();
        private NotifyIcon? _notifyIcon;
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            LocalWebSocketServer.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(string theme)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyTheme(theme);
            });
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            string theme = SettingsStore.Current.Theme ?? "dark";
            ApplyTheme(theme);
        }

        public void ApplyTheme(string theme)
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                bool isLight = theme.Equals("light", StringComparison.OrdinalIgnoreCase);

                if (isLight)
                {
                    int useDarkMode = 0;
                    if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                    }

                    // Light title bar (#F9F9F9 -> BGR 0x00F9F9F9)
                    int captionColor = 0x00F9F9F9;
                    DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                    // Dark text for light mode
                    int textColor = 0x000F0F0F;
                    DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0xF9, 0xF9));
                }
                else
                {
                    int useDarkMode = 1;
                    if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                    }

                    // Dark title bar (#181818 -> BGR 0x00181818)
                    int captionColor = 0x00181818;
                    DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                    // White text for dark mode
                    int textColor = 0x00FFFFFF;
                    DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x18));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply theme to title bar: {ex.Message}");
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Start backend HTTP and WebSocket server
            _httpServer.Start();

            // 2. Register custom protocols for extension integration
            ProtocolHandler.RegisterProtocols();

            // 3. Start artist scheduler
            ArtistScheduler.Start();

            // 4. Initialize System Tray
            InitNotifyIcon();

            // 5. Initialize WebView2
            try
            {
                string profileDir = Path.Combine(PathUtils.GetAppDataDirectory(), "WebView2_Profile");
                CoreWebView2Environment env;
                try
                {
                    env = await CoreWebView2Environment.CreateAsync(null, profileDir);
                }
                catch (Exception)
                {
                    // If locked (0x800700AA / ERROR_BUSY) or in use, fallback to a unique temp directory
                    string fallbackDir = Path.Combine(Path.GetTempPath(), $"Ssshmul_WV2_{Guid.NewGuid():N}");
                    env = await CoreWebView2Environment.CreateAsync(null, fallbackDir);
                }

                await MainWebView.EnsureCoreWebView2Async(env);

                MainWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                MainWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                MainWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                MainWebView.NavigationCompleted += (s, args) =>
                {
                    LoadingText.Visibility = Visibility.Collapsed;
                };

                MainWebView.Source = new Uri("http://localhost:9595/index.html");
            }
            catch (Exception ex)
            {
                LoadingText.Text = $"שגיאה בטעינת ממשק: {ex.Message}";
                Debug.WriteLine($"WebView2 initialization failed: {ex.Message}");
            }
        }

        private void InitNotifyIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Text = "Ssshmul Downloader",
                    Visible = true
                };

                string iconPath = PathUtils.GetIconPath();
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("פתח את Ssshmul Downloader", null, (s, e) => ShowAndActivate());
                contextMenu.Items.Add("סרוק אמנים עכשיו 🔄", null, (s, e) =>
                {
                    _ = ArtistScanner.ScanAllArtistsAsync(triggerDownload: true);
                });
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("יציאה", null, (s, e) =>
                {
                    _isExplicitExit = true;
                    Close();
                });

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (s, e) => ShowAndActivate();

                ArtistScanner.NotificationTriggered += (title, text) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _notifyIcon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
                    });
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to init NotifyIcon: {ex.Message}");
            }
        }

        private void ShowAndActivate()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isExplicitExit && SettingsStore.Current.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                _notifyIcon?.ShowBalloonTip(2500, "Ssshmul Downloader", "התוכנה פועלת ברקע ומאפשרת הורדה רציפה מהתוסף ⚡", ToolTipIcon.Info);
                return;
            }

            // Cleanup
            _notifyIcon?.Dispose();
            _httpServer.Stop();
            ArtistScheduler.Stop();
        }
    }
}
