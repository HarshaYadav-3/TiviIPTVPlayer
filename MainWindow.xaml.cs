using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Graphics;
using TiviIPTVPlayer.Helpers;
using TiviIPTVPlayer.Pages;
using TiviIPTVPlayer.Services;

namespace TiviIPTVPlayer
{
    /// <summary>
    /// Main shell window: top navigation bar (Player / Playlists / Settings / About)
    /// hosting pages in a Frame, plus theme toggle and fullscreen support.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public bool IsFullscreen { get; private set; }

        /// <summary>Raised after entering or leaving fullscreen (header visibility changes).</summary>
        public event Action? FullscreenChanged;

        /// <summary>Navigates the shell frame to a page type.</summary>
        public void NavigateTo(Type pageType, object? parameter = null)
        {
            try
            {
                if (ContentFrame.CurrentSourcePageType == pageType && parameter == null)
                {
                    return;
                }

                ContentFrame.Navigate(pageType, parameter);
                UpdateNavSelection(pageType);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"MainWindow.NavigateTo({pageType.Name})");
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            this.Title = "TiviIPTVPlayer";
            WindowHelper.SetWindowTitle(this, "TiviIPTVPlayer");
            WindowHelper.SetAppIcon(this);
            WindowHelper.Maximize(this);

            // System theme by default: RootGrid.RequestedTheme = Default follows
            // Windows, restored from the last toggle choice when present.
            RootGrid.RequestedTheme = ThemeHelper.LoadSavedTheme();
            SyncThemeToggleUI();

            try
            {
                ContentFrame.Navigate(typeof(PlayerPage));
                UpdateNavSelection(typeof(PlayerPage));
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.InitialNavigate");
            }

            AppLogger.LogAction("MainWindowCreated");
        }

        private void UpdateNavSelection(Type pageType)
        {
            try
            {
                var map = new (Button button, Type page)[]
                {
                    (NavPlayerButton, typeof(PlayerPage)),
                    (NavPlaylistsButton, typeof(PlaylistsPage)),
                    (NavSettingsButton, typeof(SettingsPage)),
                    (NavAboutButton, typeof(AboutPage)),
                };

                foreach (var (button, page) in map)
                {
                    bool selected = page == pageType;
                    button.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                    button.BorderThickness = selected ? new Thickness(0, 0, 0, 2) : new Thickness(0);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "UpdateNavSelection");
            }
        }

        private void OnNavClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button || button.Tag is not string tag)
                {
                    return;
                }

                Type? page = tag switch
                {
                    "player" => typeof(PlayerPage),
                    "playlists" => typeof(PlaylistsPage),
                    "settings" => typeof(SettingsPage),
                    "about" => typeof(AboutPage),
                    _ => null,
                };
                if (page != null)
                {
                    NavigateTo(page);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnNavClick");
            }
        }

        #region Theme

        /// <summary>Effective Light/Dark after resolving System via Windows.</summary>
        public ElementTheme EffectiveTheme => ThemeHelper.GetEffectiveTheme(RootGrid);

        /// <summary>Currently requested theme (Default = follow Windows).</summary>
        public ElementTheme CurrentRequestedTheme => RootGrid.RequestedTheme;

        private void SyncThemeToggleUI()
        {
            if (RootGrid.RequestedTheme == ElementTheme.Light)
            {
                ThemeGlyphIcon.Glyph = "\uE706"; // Light / Brightness
                ThemeTextBlock.Text = "Light";
                ToolTipService.SetToolTip(ThemeToggleButton, "Light theme");
            }
            else if (RootGrid.RequestedTheme == ElementTheme.Dark)
            {
                ThemeGlyphIcon.Glyph = "\uE708"; // Dark / QuietHours (moon)
                ThemeTextBlock.Text = "Dark";
                ToolTipService.SetToolTip(ThemeToggleButton, "Dark theme");
            }
            else
            {
                ThemeGlyphIcon.Glyph = "\uF08C"; // System
                var system = ThemeHelper.GetSystemTheme();
                ThemeTextBlock.Text = "System";
                ToolTipService.SetToolTip(ThemeToggleButton, $"System theme (Windows is {system})");
            }
        }

        private void OnThemeToggleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cycle: System (Default) -> Light -> Dark -> System ...
                RootGrid.RequestedTheme = ThemeHelper.NextTheme(RootGrid.RequestedTheme);
                ThemeHelper.SaveTheme(RootGrid.RequestedTheme);
                SyncThemeToggleUI();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnThemeToggleClick");
            }
        }

        /// <summary>Applies a theme picked from the Settings page.</summary>
        public void ApplyTheme(ElementTheme theme)
        {
            RootGrid.RequestedTheme = theme;
            ThemeHelper.SaveTheme(theme);
            SyncThemeToggleUI();
        }

        #endregion

        #region Fullscreen

        /// <summary>Toggles borderless OS fullscreen for the whole window.</summary>
        public void SetFullscreen(bool enable)
        {
            try
            {
                var appWindow = AppWindow;
                if (appWindow == null)
                {
                    return;
                }

                if (enable)
                {
                    appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                }
                else
                {
                    appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                }

                IsFullscreen = enable;
                HeaderRow.Height = new GridLength(enable ? 0 : 52);
                FullscreenChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"SetFullscreen({enable})");
            }
        }

        #endregion
    }
}
