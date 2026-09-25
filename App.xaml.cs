using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using TiviIPTVPlayer.Services;

namespace TiviIPTVPlayer
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        // Single-instance identity.
        private const string MutexName = "Local\\TiviIPTVPlayer_SingleInstance_7E38B5D2";
        internal const string RestoreMessageName = "TiviIPTVPlayerRestoreMessage_7E38B5D2";
        private const string MainWindowTitle = "TiviIPTVPlayer"; // must match Title in MainWindow.xaml

        private static Mutex? _mutex;
        private Window? _window;

        /// <summary>Shell window reference for pages (fullscreen toggling etc.).</summary>
        public static MainWindow? MainWindow { get; private set; }

        #region Win32 P/Invoke for Instance Restoration

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        #endregion

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            InitializeComponent();
            try
            {
                Resources["ContentDialogMaxWidth"] = 1000.0;
            }
            catch (Exception)
            {
            }

            UnhandledException += (sender, args) =>
            {
                AppLogger.LogException(args.Exception, "GlobalUnhandledException");
                args.Handled = true;
            };
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                // Another instance is already running. Signal it to restore and exit.
                RestoreExistingInstance();
                Application.Current.Exit();
                return;
            }

            SyncStartupSetting();

            _window = new MainWindow();
            MainWindow = (MainWindow)_window;
            _window.Activate();
        }

        #region Single-instance restore + startup sync

        private static void RestoreExistingInstance()
        {
            try
            {
                IntPtr hwnd = FindWindow(null, MainWindowTitle);
                if (hwnd != IntPtr.Zero)
                {
                    uint msg = RegisterWindowMessage(RestoreMessageName);
                    if (msg != 0)
                    {
                        PostMessage(hwnd, msg, IntPtr.Zero, IntPtr.Zero);
                    }

                    ShowWindow(hwnd, SW_RESTORE);
                    ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow(hwnd);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RestoreExistingInstance");
            }
        }

        private static void SyncStartupSetting()
        {
            try
            {
                bool runAtStartup = SettingsService.IsRunAtStartup;
                new StartupManager().SetAutoStart(runAtStartup);
            }
            catch (Exception) { }
        }

        #endregion
    }
}
