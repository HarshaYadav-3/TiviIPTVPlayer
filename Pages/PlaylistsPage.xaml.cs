using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TiviIPTVPlayer.Models;
using TiviIPTVPlayer.Services;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;

namespace TiviIPTVPlayer.Pages
{
    /// <summary>Full playlist source management page.</summary>
    public sealed partial class PlaylistsPage : Page
    {
        private readonly PlaylistManager _playlists = PlaylistManager.Instance;
        private PlaylistSource? _epgTarget;
        private PlaylistSource? _renameTarget;
        private bool _initialized;

        public PlaylistsPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;

            _playlists.SourcesChanged += OnSourcesChanged;
            _playlists.RefreshStateChanged += OnRefreshStateChanged;

            AppLogger.LogAction("PlaylistsPageConstructed");
        }

        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            try
            {
                if (_playlists.Sources.Count == 0)
                {
                    await _playlists.InitializeAsync();
                }

                RefreshList();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PlaylistsPage.OnPageLoaded");
            }
        }

        private void OnSourcesChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshList);
        }

        private void OnRefreshStateChanged(PlaylistSource source, bool started)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                PageBusyRing.IsActive = started;
                PageBusyText.Text = started ? $"Refreshing {source.Name}…" : string.Empty;
            });
        }

        private void RefreshList()
        {
            try
            {
                SourcesList.ItemsSource = _playlists.Sources.ToList();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PlaylistsPage.RefreshList");
            }
        }

        // ------------------------------------------------------------ actions

        private void OnActivateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement { DataContext: PlaylistSource source })
                {
                    _playlists.SetActive(source.Id);
                    RefreshList();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnActivateClick");
            }
        }

        private async void OnRefreshItemClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement { DataContext: PlaylistSource source })
                {
                    return;
                }

                var result = await _playlists.RefreshAsync(source);
                RefreshList();
                if (!result.Success)
                {
                    await ShowDialogAsync("Refresh failed",
                        $"“{source.Name}” could not be refreshed.\n\n{result.Error}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnRefreshItemClick");
            }
        }

        private async void OnEpgClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement { DataContext: PlaylistSource source })
                {
                    return;
                }

                _epgTarget = source;
                EpgTargetName.Text = source.Name;
                EpgUrlBox.Text = source.EpgUrl;
                EpgErrorBar.IsOpen = false;
                EpgProgress.IsActive = false;
                EpgDialog.XamlRoot = XamlRoot;
                await EpgDialog.ShowQueuedAsync();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnEpgClick");
            }
        }

        private async void OnEpgConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_epgTarget == null)
            {
                return;
            }

            var deferral = args.GetDeferral();
            try
            {
                var url = EpgUrlBox.Text.Trim();
                if (url.Length > 0)
                {
                    bool valid = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                                 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                    if (!valid)
                    {
                        args.Cancel = true;
                        EpgErrorBar.Message = "Enter a valid http(s) URL, or leave the field empty to disable EPG.";
                        EpgErrorBar.IsOpen = true;
                        return;
                    }
                }

                EpgProgress.IsActive = true;
                await _playlists.SetEpgUrlAsync(_epgTarget, url);
                EpgProgress.IsActive = false;
                RefreshList();
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                EpgProgress.IsActive = false;
                EpgErrorBar.Message = ex.Message;
                EpgErrorBar.IsOpen = true;
                AppLogger.LogException(ex, "OnEpgConfirm");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void OnRenameClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement { DataContext: PlaylistSource source })
                {
                    return;
                }

                _renameTarget = source;
                RenameBox.Text = source.Name;
                RenameDialog.XamlRoot = XamlRoot;
                await RenameDialog.ShowQueuedAsync();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnRenameClick");
            }
        }

        private async void OnRenameConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_renameTarget == null)
            {
                return;
            }

            try
            {
                var name = RenameBox.Text.Trim();
                if (name.Length > 0 && !string.Equals(name, _renameTarget.Name, StringComparison.Ordinal))
                {
                    await _playlists.RenameSourceAsync(_renameTarget, name);
                    RefreshList();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnRenameConfirm");
            }
        }

        private async void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement { DataContext: PlaylistSource source })
                {
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "Remove playlist",
                    Content = $"Remove “{source.Name}” and its cached channels?\nFavorites are kept.",
                    PrimaryButtonText = "Remove",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot,
                };
                if (await dialog.ShowQueuedAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var error = await _playlists.RemoveSourceAsync(source);
                if (!string.IsNullOrEmpty(error))
                {
                    await ShowDialogAsync("Cannot remove", error);
                }

                RefreshList();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnRemoveClick");
            }
        }

        // Add URL / import file -------------------------------------------

        private async void OnAddUrlClick(object sender, RoutedEventArgs e)
        {
            try
            {
                AddUrlBox.Text = string.Empty;
                AddNameBox.Text = string.Empty;
                AddUrlErrorBar.IsOpen = false;
                AddUrlProgress.IsActive = false;
                AddUrlDialog.IsPrimaryButtonEnabled = false;
                AddUrlDialog.XamlRoot = XamlRoot;
                await AddUrlDialog.ShowQueuedAsync();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnAddUrlClick");
            }
        }

        private void OnAddUrlBoxChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var text = AddUrlBox.Text.Trim();
                bool valid = Uri.TryCreate(text, UriKind.Absolute, out var uri)
                             && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                AddUrlDialog.IsPrimaryButtonEnabled = valid;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnAddUrlBoxChanged");
            }
        }

        private async void OnAddUrlConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                AddUrlProgress.IsActive = true;
                AddUrlErrorBar.IsOpen = false;

                var (source, error) = await _playlists.AddUrlPlaylistAsync(
                    AddUrlBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(AddNameBox.Text) ? null : AddNameBox.Text.Trim());

                AddUrlProgress.IsActive = false;

                if (source == null)
                {
                    args.Cancel = true;
                    AddUrlErrorBar.Message = error;
                    AddUrlErrorBar.IsOpen = true;
                }
                else
                {
                    RefreshList();
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                AddUrlProgress.IsActive = false;
                AddUrlErrorBar.Message = ex.Message;
                AddUrlErrorBar.IsOpen = true;
                AppLogger.LogException(ex, "OnAddUrlConfirm");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async void OnOpenFileClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    ViewMode = PickerViewMode.List,
                };
                picker.FileTypeFilter.Add(".m3u");
                picker.FileTypeFilter.Add(".m3u8");
                picker.FileTypeFilter.Add(".txt");

                if (App.MainWindow != null)
                {
                    InitializeWithWindow.Initialize(picker,
                        WindowNative.GetWindowHandle(App.MainWindow));
                }

                var file = await picker.PickSingleFileAsync();
                if (file == null)
                {
                    return;
                }

                PageBusyRing.IsActive = true;
                PageBusyText.Text = $"Importing {file.Name}…";

                var (source, error) = await _playlists.ImportFilePlaylistAsync(file.Path);

                PageBusyRing.IsActive = false;
                PageBusyText.Text = string.Empty;

                if (source == null)
                {
                    await ShowDialogAsync("Import failed", error);
                    return;
                }

                RefreshList();
            }
            catch (Exception ex)
            {
                PageBusyRing.IsActive = false;
                PageBusyText.Text = string.Empty;
                AppLogger.LogException(ex, "PlaylistsPage.OnOpenFileClick");
                await ShowDialogAsync("Import failed", ex.Message);
            }
        }

        private async Task ShowDialogAsync(string title, string message)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot,
                };
                await dialog.ShowQueuedAsync();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ShowDialogAsync");
            }
        }
    }
}
