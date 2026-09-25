using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using TiviIPTVPlayer.Services;

namespace TiviIPTVPlayer.Pages
{
    /// <summary>App settings page.</summary>
    public sealed partial class SettingsPage : Page
    {
        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
            AppLogger.LogAction("SettingsPageConstructed");
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize control states from code (never via inline XAML selection).
                _loading = true;

                var requested = App.MainWindow?.CurrentRequestedTheme ?? ElementTheme.Default;
                ThemeCombo.SelectedIndex = requested switch
                {
                    ElementTheme.Light => 1,
                    ElementTheme.Dark => 2,
                    _ => 0,
                };

                ResumeToggle.IsOn = SettingsService.ResumeLastChannel;
                AutoRefreshToggle.IsOn = SettingsService.AutoRefreshOnLaunch;
                LoadEpgToggle.IsOn = SettingsService.LoadEpg;
                StartupToggle.IsOn = SettingsService.IsRunAtStartup;

                _loading = false;
                UpdateCacheSize();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.OnPageLoaded");
            }
        }

        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_loading || !_contentLoaded || ThemeCombo.SelectedItem is not ComboBoxItem item)
                {
                    return;
                }

                var theme = (item.Tag as string) switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
                App.MainWindow?.ApplyTheme(theme);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnThemeChanged");
            }
        }

        private void OnResumeToggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
            {
                SettingsService.ResumeLastChannel = ResumeToggle.IsOn;
            }
        }

        private void OnAutoRefreshToggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
            {
                SettingsService.AutoRefreshOnLaunch = AutoRefreshToggle.IsOn;
            }
        }

        private void OnLoadEpgToggled(object sender, RoutedEventArgs e)
        {
            if (!_loading)
            {
                SettingsService.LoadEpg = LoadEpgToggle.IsOn;
            }
        }

        private void OnStartupToggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            try
            {
                SettingsService.IsRunAtStartup = StartupToggle.IsOn;
                new StartupManager().SetAutoStart(StartupToggle.IsOn);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnStartupToggled");
            }
        }

        private void UpdateCacheSize()
        {
            long bytes = SettingsService.CacheSizeBytes();
            CacheSizeText.Text = $"Playlist cache: {FormatSize(bytes)}";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:N0} KB";
            }

            return $"{bytes / (1024.0 * 1024.0):N1} MB";
        }

        private async void OnClearCacheClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Clear playlist cache",
                    Content = "Delete the cached playlist files? The next launch will re-download them from the source.",
                    PrimaryButtonText = "Clear",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                if (await dialog.ShowQueuedAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var dir = AppPaths.PlaylistsFolder;
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*.m3u"))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                        }
                    }
                }

                UpdateCacheSize();
                StorageInfoBar.Severity = InfoBarSeverity.Success;
                StorageInfoBar.Message = "Playlist cache cleared.";
                StorageInfoBar.IsOpen = true;
                AppLogger.LogAction("CacheCleared");
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnClearCacheClick");
                StorageInfoBar.Severity = InfoBarSeverity.Error;
                StorageInfoBar.Message = ex.Message;
                StorageInfoBar.IsOpen = true;
            }
        }

        private async void OnClearFavoritesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Clear favorites",
                    Content = "Remove all favorite channels? This cannot be undone.",
                    PrimaryButtonText = "Clear",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                if (await dialog.ShowQueuedAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                FavoritesService.Instance.ClearAll();
                StorageInfoBar.Severity = InfoBarSeverity.Success;
                StorageInfoBar.Message = "All favorites removed.";
                StorageInfoBar.IsOpen = true;
                AppLogger.LogAction("FavoritesCleared");
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnClearFavoritesClick");
            }
        }
    }
}
