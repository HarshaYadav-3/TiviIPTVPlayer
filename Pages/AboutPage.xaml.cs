using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Reflection;
using TiviIPTVPlayer.Services;
using Windows.System;

namespace TiviIPTVPlayer.Pages
{
    /// <summary>About page: app info and credits.</summary>
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var version = TryGetVersion();
                VersionText.Text = $"Version {version}";
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "AboutPage.OnPageLoaded");
            }
        }

        private static string TryGetVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v != null)
                {
                    return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                }
            }
            catch
            {
            }
            return "1.0.0.0";
        }


        private async void OnOpenSnapshotsFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = AppPaths.SnapshotsFolder;
                await Launcher.LaunchFolderPathAsync(dir);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnOpenSnapshotsFolder");
            }
        }
    }
}
