using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TiviIPTVPlayer.Converters;
using TiviIPTVPlayer.Models;
using TiviIPTVPlayer.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace TiviIPTVPlayer.Pages
{
    /// <summary>
    /// Main screen: categories + channel browser (left), live player with
    /// playback controls and EPG line (center), playlist management and
    /// now-playing guide (right).
    /// </summary>
    public sealed partial class PlayerPage : Page
    {
        private readonly PlaylistManager _playlists = PlaylistManager.Instance;
        private readonly FavoritesService _favorites = FavoritesService.Instance;

        private MediaPlayer? _player;
        private IptvChannel? _currentChannel;
        private IptvChannel? _contextChannel;
        private IptvChannel? _detailsChannel;
        private CategoryItem _currentCategory = new() { Kind = CategoryKind.All };
        private string _searchText = string.Empty;
        private List<IptvChannel> _allChannels = new();
        private string? _pendingResumeChannelId;
        private DispatcherTimer? _epgTimer;
        private DispatcherTimer? _searchDebounce;
        private bool _initialized;
        private bool _updatingSources;
        private bool _suppressVolumeEvents;
        private string _lastSnapshotDir = string.Empty;

        public PlayerPage()
        {
            InitializeComponent();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            SetupPlayer();
            WireEvents();

            SizeChanged += (_, _) => ApplyLayoutMode();
            Loaded += OnPageLoadedFirstTime;

            // ContentDialog wiring that cannot be expressed in XAML page resources.
            ChannelDetailsDialog.PrimaryButtonClick += OnDetailsWatchClick;

            AppLogger.LogAction("PlayerPageConstructed");
        }

        // ------------------------------------------------------------- setup

        private void SetupPlayer()
        {
            try
            {
                _player = new MediaPlayer
                {
                    AutoPlay = true,
                    Volume = SettingsService.Volume,
                    IsMuted = SettingsService.Muted,
                };
                _player.MediaOpened += OnMediaOpened;
                _player.MediaFailed += OnMediaFailed;
                _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
                VideoPlayer.SetMediaPlayer(_player);

                _suppressVolumeEvents = true;
                VolumeSlider.Value = SettingsService.Volume * 100;
                _suppressVolumeEvents = false;
                SyncMuteGlyph();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SetupPlayer");
            }
        }

        private void WireEvents()
        {
            _playlists.ActiveChannelsChanged += OnActiveChannelsChanged;
            _playlists.SourcesChanged += OnSourcesChanged;
            _playlists.RefreshStateChanged += OnRefreshStateChanged;
            _playlists.EpgChanged += OnEpgChanged;
            _favorites.FavoritesChanged += OnFavoritesChanged;

            if (App.MainWindow != null)
            {
                App.MainWindow.FullscreenChanged += ApplyLayoutMode;
            }

            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                ApplyFilters();
            };

            _epgTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _epgTimer.Tick += (_, _) => UpdateEpgUi();
        }

        private async void OnPageLoadedFirstTime(object sender, RoutedEventArgs e)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            Loaded -= OnPageLoadedFirstTime;

            try
            {
                ApplyLayoutMode();
                await _playlists.InitializeAsync();
                LoadChannelsFromManager(markFavorites: true, rebuildCategories: true);

                if (SettingsService.ResumeLastChannel && !string.IsNullOrEmpty(SettingsService.LastChannelId))
                {
                    _pendingResumeChannelId = SettingsService.LastChannelId;
                    TryResumePendingChannel();
                }

                if (_playlists.ActiveSource != null && SettingsService.AutoRefreshOnLaunch)
                {
                    _ = RefreshActivePlaylistAsync();
                }
                else
                {
                    UpdatePlaylistStatus();
                }

                _epgTimer?.Start();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PlayerPage.OnPageLoadedFirstTime");
            }
        }

        // ------------------------------------------------------- data binding

        private void OnActiveChannelsChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    LoadChannelsFromManager(markFavorites: true, rebuildCategories: true);
                    TryResumePendingChannel();
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "OnActiveChannelsChanged");
                }
            });
        }

        private void OnSourcesChanged()
        {
            DispatcherQueue.TryEnqueue(UpdateSourcesCombo);
        }

        private void OnRefreshStateChanged(PlaylistSource source, bool started)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    bool active = source.Id == _playlists.ActiveSource?.Id;
                    RefreshProgress.IsActive = started && active;
                    RefreshProgress.Visibility = started && active ? Visibility.Visible : Visibility.Collapsed;
                    RefreshIcon.Visibility = started && active ? Visibility.Collapsed : Visibility.Visible;
                    if (started && active)
                    {
                        PlaylistStatusText.Text = "Refreshing playlist…";
                        PlaylistStatusText.Foreground = (Brush)Application.Current.Resources["PanelHeaderTextBrush"];
                    }
                    else
                    {
                        UpdatePlaylistStatus();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "OnRefreshStateChanged");
                }
            });
        }

        private void OnEpgChanged(string sourceId)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdatePlaylistStatus();
                UpdateEpgUi();
            });
        }

        private void OnFavoritesChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    foreach (var channel in _allChannels)
                    {
                        channel.IsFavorite = _favorites.IsFavorite(channel);
                    }

                    RebuildCategories();
                    if (_currentCategory.Kind == CategoryKind.Favorites)
                    {
                        ApplyFilters();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "OnFavoritesChanged");
                }
            });
        }

        private void LoadChannelsFromManager(bool markFavorites, bool rebuildCategories)
        {
            _allChannels = _playlists.Channels.ToList();
            if (markFavorites)
            {
                foreach (var channel in _allChannels)
                {
                    channel.IsFavorite = _favorites.IsFavorite(channel);
                }
            }

            UpdateSourcesCombo();
            if (rebuildCategories)
            {
                RebuildCategories();
            }

            ApplyFilters();
        }

        private void UpdateSourcesCombo()
        {
            try
            {
                _updatingSources = true;
                SourcesCombo.ItemsSource = _playlists.Sources.ToList();
                SourcesCombo.SelectedItem = _playlists.ActiveSource;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "UpdateSourcesCombo");
            }
            finally
            {
                _updatingSources = false;
            }
        }

        private void RebuildCategories()
        {
            try
            {
                var items = new List<CategoryItem>
                {
                    new() { Kind = CategoryKind.All, Count = _allChannels.Count },
                    new() { Kind = CategoryKind.Favorites, Count = _allChannels.Count(c => c.IsFavorite) },
                };

                items.AddRange(_allChannels
                    .Where(c => !string.IsNullOrWhiteSpace(c.Group))
                    .GroupBy(c => c.Group, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new CategoryItem { Kind = CategoryKind.Group, Name = g.Key, Count = g.Count() }));

                CategoriesList.ItemsSource = items;

                // Restore or default the selection (never set selection in XAML).
                CategoryItem? restore = items.FirstOrDefault(MatchesCurrentCategory);
                CategoriesList.SelectedItem = restore ?? items[0];
                _currentCategory = (CategoryItem)CategoriesList.SelectedItem;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RebuildCategories");
            }
        }

        private bool MatchesCurrentCategory(CategoryItem item) => item.Kind == _currentCategory.Kind
            && (item.Kind != CategoryKind.Group || string.Equals(item.Name, _currentCategory.Name, StringComparison.OrdinalIgnoreCase));

        private void ApplyFilters()
        {
            try
            {
                IEnumerable<IptvChannel> query;

                // A search is global across the whole playlist; without search
                // text the selected category applies.
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    query = _allChannels.Where(c =>
                        c.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
                }
                else if (_currentCategory.Kind == CategoryKind.Favorites)
                {
                    query = _allChannels.Where(c => c.IsFavorite);
                }
                else if (_currentCategory.Kind == CategoryKind.Group)
                {
                    query = _allChannels.Where(c =>
                        string.Equals(c.Group, _currentCategory.Name, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    query = _allChannels;
                }

                var filtered = query.ToList();
                ChannelsList.ItemsSource = filtered;

                bool listEmpty = filtered.Count == 0;
                ChannelsEmptyPanel.Visibility = listEmpty ? Visibility.Visible : Visibility.Collapsed;
                ChannelsLoadingPanel.Visibility = Visibility.Collapsed;

                if (listEmpty)
                {
                    if (_allChannels.Count == 0)
                    {
                        ChannelsEmptyText.Text = "No channels loaded yet.\nUse Refresh or add a playlist.";
                    }
                    else if (_currentCategory.Kind == CategoryKind.Favorites)
                    {
                        ChannelsEmptyText.Text = "No favorites yet.\nTap the star next to a channel to add it.";
                    }
                    else
                    {
                        ChannelsEmptyText.Text = $"No channels match “{_searchText}”.";
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ApplyFilters");
            }
        }

        // ------------------------------------------------------------ playlist

        private void OnSourceComboChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_updatingSources || !_contentLoaded)
                {
                    return;
                }

                if (SourcesCombo.SelectedItem is PlaylistSource source && source.Id != _playlists.ActiveSource?.Id)
                {
                    _playlists.SetActive(source.Id);
                    LoadChannelsFromManager(markFavorites: true, rebuildCategories: true);
                    UpdatePlaylistStatus();
                    _ = RefreshActivePlaylistAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnSourceComboChanged");
            }
        }

        private async Task RefreshActivePlaylistAsync()
        {
            var source = _playlists.ActiveSource;
            if (source == null)
            {
                return;
            }

            try
            {
                ChannelsLoadingPanel.Visibility = _playlists.Channels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                ChannelsLoadingText.Text = $"Loading {source.Name}…";

                var result = await _playlists.RefreshAsync(source);
                ChannelsLoadingPanel.Visibility = Visibility.Collapsed;

                if (!result.Success)
                {
                    PlaylistStatusText.Text = $"Refresh failed: {result.Error}";
                    PlaylistStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 100, 100));
                }
                else
                {
                    UpdatePlaylistStatus();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RefreshActivePlaylistAsync");
            }
        }

        private void UpdatePlaylistStatus()
        {
            try
            {
                var source = _playlists.ActiveSource;
                if (source == null)
                {
                    PlaylistStatusText.Text = string.Empty;
                    return;
                }

                PlaylistStatusText.Foreground = (Brush)Application.Current.Resources["PanelHeaderTextBrush"];
                var updated = source.LastRefreshedUtc.HasValue
                    ? source.LastRefreshedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : "never refreshed";

                string epgStatus;
                var epg = _playlists.ActiveEpg;
                if (epg == null || epg.ChannelCount == 0)
                {
                    epgStatus = string.IsNullOrEmpty(source.EpgUrl)
                        ? "No EPG guide provided by this playlist"
                        : "EPG guide not loaded";
                }
                else
                {
                    epgStatus = $"EPG: {epg.ChannelCount:N0} channels, {epg.ProgrammeCount:N0} programmes";
                }

                PlaylistStatusText.Text = $"{ChannelConverters.PluralChannels(source.ChannelCount)} · {updated}\n{epgStatus}";
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "UpdatePlaylistStatus");
            }
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            _ = RefreshActivePlaylistAsync();
        }

        // Add URL dialog ------------------------------------------------

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
                    _playlists.SetActive(source.Id);
                    LoadChannelsFromManager(markFavorites: true, rebuildCategories: true);
                    UpdatePlaylistStatus();
                    AppLogger.LogAction("AddUrlDialogConfirmed", new Dictionary<string, object>
                    {
                        { "name", source.Name },
                    });
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

        // Open file -----------------------------------------------------

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

                ChannelsLoadingPanel.Visibility = Visibility.Visible;
                ChannelsLoadingText.Text = $"Importing {file.Name}…";

                var (source, error) = await _playlists.ImportFilePlaylistAsync(file.Path);
                ChannelsLoadingPanel.Visibility = Visibility.Collapsed;

                if (source == null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Import failed",
                        Content = error,
                        CloseButtonText = "OK",
                        XamlRoot = XamlRoot,
                    };
                    await dialog.ShowQueuedAsync();
                    return;
                }

                _playlists.SetActive(source.Id);
                LoadChannelsFromManager(markFavorites: true, rebuildCategories: true);
                UpdatePlaylistStatus();
            }
            catch (Exception ex)
            {
                ChannelsLoadingPanel.Visibility = Visibility.Collapsed;
                AppLogger.LogException(ex, "OnOpenFileClick");
                await ShowErrorDialogAsync("Import failed", ex.Message);
            }
        }

        // ------------------------------------------------------------ channels

        private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (!_contentLoaded || CategoriesList == null)
                {
                    return;
                }

                if (e.AddedItems.Count == 0)
                {
                    return;
                }

                if (CategoriesList.SelectedItem is CategoryItem item)
                {
                    _currentCategory = item;
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnCategorySelectionChanged");
            }
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchText = SearchBox.Text.Trim();
                _searchDebounce?.Stop();
                _searchDebounce?.Start();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnSearchChanged");
            }
        }

        private void OnChannelItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is IptvChannel channel)
            {
                PlayChannel(channel);
            }
        }

        private void OnChannelsRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            try
            {
                var node = e.OriginalSource as DependencyObject;
                while (node != null && node is not ListViewItem && node is not ListView)
                {
                    node = VisualTreeHelper.GetParent(node);
                }

                if (node is ListViewItem item && item.Content is IptvChannel channel)
                {
                    _contextChannel = channel;
                    item.IsSelected = true;
                }
                else
                {
                    _contextChannel = null;
                    e.Handled = true; // no flyout on empty space
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnChannelsRightTapped");
            }
        }

        private void OnMenuPlayClick(object sender, RoutedEventArgs e)
        {
            if (_contextChannel != null)
            {
                PlayChannel(_contextChannel);
            }
        }

        private void OnMenuFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (_contextChannel != null)
            {
                ToggleFavorite(_contextChannel);
            }
        }

        private async void OnMenuDetailsClick(object sender, RoutedEventArgs e)
        {
            if (_contextChannel != null)
            {
                await ShowChannelDetailsAsync(_contextChannel);
            }
        }

        private void OnMenuCopyUrlClick(object sender, RoutedEventArgs e)
        {
            if (_contextChannel != null)
            {
                CopyToClipboard(_contextChannel.Url);
            }
        }

        private void OnStarClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement element && element.DataContext is IptvChannel channel)
                {
                    ToggleFavorite(channel);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnStarClick");
            }
        }

        private void ToggleFavorite(IptvChannel channel)
        {
            _favorites.Toggle(channel);
            if (_currentCategory.Kind == CategoryKind.Favorites)
            {
                ApplyFilters();
            }
        }

        // ------------------------------------------------------------ playback

        private void PlayChannel(IptvChannel channel)
        {
            try
            {
                if (_player == null)
                {
                    return;
                }

                _currentChannel = channel;
                SettingsService.LastChannelId = channel.Id;

                EmptyOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingText.Text = "Opening stream…";

                NowChannelName.Text = channel.Name;
                NowLogo.Source = ChannelConverters.Logo(channel.TvgLogo);
                NowName.Text = channel.Name;
                NowGroup.Text = string.IsNullOrWhiteSpace(channel.Group) ? "Uncategorized" : channel.Group;

                UpdateEpgUi();

                _player.Source = MediaSource.CreateFromUri(new Uri(channel.Url));
                _player.Play();

                AppLogger.LogAction("PlayChannel", new Dictionary<string, object>
                {
                    { "channel", channel.Name },
                    { "group", channel.Group },
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PlayChannel");
                ShowPlayerError("Playback failed", ex.Message);
            }
        }

        private void TryResumePendingChannel()
        {
            if (_pendingResumeChannelId == null || _allChannels.Count == 0)
            {
                return;
            }

            var channel = _allChannels.FirstOrDefault(c => c.Id == _pendingResumeChannelId);
            _pendingResumeChannelId = null;
            if (channel != null)
            {
                PlayChannel(channel);
            }
        }

        private void OnMediaOpened(MediaPlayer sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Collapsed;
                EmptyOverlay.Visibility = Visibility.Collapsed;
            });
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var detail = !string.IsNullOrWhiteSpace(args.ErrorMessage)
                    ? args.ErrorMessage
                    : args.ExtendedErrorCode?.Message ?? "The stream did not respond.";
                ShowPlayerError("Channel unavailable",
                    $"“{_currentChannel?.Name ?? "This channel"}” could not be played.\n{detail}");
                AppLogger.LogAction("MediaFailed", new Dictionary<string, object>
                {
                    { "channel", _currentChannel?.Name ?? "" },
                    { "error", detail },
                    { "extended", args.ExtendedErrorCode?.Message ?? "" },
                });
            });
        }

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    switch (sender.PlaybackState)
                    {
                        case MediaPlaybackState.Playing:
                            LoadingOverlay.Visibility = Visibility.Collapsed;
                            PlayPauseGlyph.Glyph = ""; // Pause
                            break;
                        case MediaPlaybackState.Paused:
                            LoadingOverlay.Visibility = Visibility.Collapsed;
                            PlayPauseGlyph.Glyph = ""; // Play
                            break;
                        case MediaPlaybackState.Buffering:
                            if (_currentChannel != null)
                            {
                                LoadingOverlay.Visibility = Visibility.Visible;
                                LoadingText.Text = "Buffering…";
                            }
                            break;
                        case MediaPlaybackState.Opening:
                            if (_currentChannel != null)
                            {
                                LoadingOverlay.Visibility = Visibility.Visible;
                                LoadingText.Text = "Opening stream…";
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "OnPlaybackStateChanged");
                }
            });
        }

        private void ShowPlayerError(string title, string detail)
        {
            ErrorTitle.Text = title;
            ErrorDetail.Text = detail;
            ErrorOverlay.Visibility = Visibility.Visible;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_player?.PlaybackSession == null)
                {
                    return;
                }

                if (_currentChannel == null)
                {
                    if (ChannelsList.SelectedItem is IptvChannel channel)
                    {
                        PlayChannel(channel);
                    }
                    return;
                }

                if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    _player.Pause();
                }
                else
                {
                    _player.Play();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnPlayPauseClick");
            }
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _currentChannel = null;
                if (_player != null)
                {
                    _player.Source = null;
                }

                NowChannelName.Text = string.Empty;
                EpgLine.Text = string.Empty;
                NowName.Text = string.Empty;
                NowGroup.Text = string.Empty;
                NowLogo.Source = null;
                NowCurrentText.Text = string.Empty;
                NowNextText.Text = string.Empty;
                NowDescText.Text = string.Empty;
                LoadingOverlay.Visibility = Visibility.Collapsed;
                ErrorOverlay.Visibility = Visibility.Collapsed;
                EmptyOverlay.Visibility = Visibility.Visible;
                PlayPauseGlyph.Glyph = "";
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnStopClick");
            }
        }

        private void OnRetryClick(object sender, RoutedEventArgs e)
        {
            if (_currentChannel != null)
            {
                PlayChannel(_currentChannel);
            }
            else
            {
                ErrorOverlay.Visibility = Visibility.Collapsed;
                EmptyOverlay.Visibility = Visibility.Visible;
            }
        }

        // -------------------------------------------------------------- volume

        private void OnVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try
            {
                if (_suppressVolumeEvents || _player == null)
                {
                    return;
                }

                _player.Volume = e.NewValue / 100.0;
                SettingsService.Volume = e.NewValue / 100.0;
                if (e.NewValue > 0 && _player.IsMuted)
                {
                    _player.IsMuted = false;
                    SettingsService.Muted = false;
                    SyncMuteGlyph();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnVolumeChanged");
            }
        }

        private void OnMuteClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_player == null)
                {
                    return;
                }

                _player.IsMuted = !_player.IsMuted;
                SettingsService.Muted = _player.IsMuted;
                SyncMuteGlyph();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnMuteClick");
            }
        }

        private void SyncMuteGlyph()
        {
            if (MuteGlyph != null)
            {
                MuteGlyph.Glyph = SettingsService.Muted ? "" : "";
            }
        }

        // ----------------------------------------------------------- fullscreen

        private void OnFullscreenClick(object sender, RoutedEventArgs e) => SetFullscreen(true);

        private void OnExitFullscreenClick(object sender, RoutedEventArgs e) => SetFullscreen(false);

        private void OnPlayerDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (App.MainWindow?.IsFullscreen == true)
            {
                SetFullscreen(false);
            }
            else
            {
                SetFullscreen(true);
            }
        }

        private void SetFullscreen(bool enable)
        {
            try
            {
                App.MainWindow?.SetFullscreen(enable);
                ApplyLayoutMode();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"SetFullscreen({enable})");
            }
        }

        private void OnSpaceAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                if (FocusIsOnTextInput())
                {
                    args.Handled = true; // let the keystroke reach the text box
                    return;
                }

                OnPlayPauseClick(this, new RoutedEventArgs());
                args.Handled = true;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnSpaceAccelerator");
            }
        }

        private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            try
            {
                if (App.MainWindow?.IsFullscreen == true)
                {
                    SetFullscreen(false);
                    args.Handled = true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnEscapeAccelerator");
            }
        }

        private bool FocusIsOnTextInput()
        {
            try
            {
                var focused = FocusManager.GetFocusedElement();
                return focused is TextBox or RichEditBox or PasswordBox;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Responsive layout: 3 panes / 2 panes / stacked, plus fullscreen.</summary>
        private void ApplyLayoutMode()
        {
            try
            {
                if (!_contentLoaded || MainGrid == null)
                {
                    return;
                }

                bool fullscreen = App.MainWindow?.IsFullscreen == true;
                double width = ActualWidth;

                if (fullscreen)
                {
                    LeftPanel.Visibility = Visibility.Collapsed;
                    RightPanel.Visibility = Visibility.Collapsed;
                    MainGrid.ColumnDefinitions[0].Width = new GridLength(0);
                    MainGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    MainGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                    MainGrid.RowDefinitions[1].Height = new GridLength(0);
                    ExitFullscreenButton.Visibility = Visibility.Visible;
                    return;
                }

                ExitFullscreenButton.Visibility = Visibility.Collapsed;

                if (width >= 1250)
                {
                    LeftPanel.Visibility = Visibility.Visible;
                    RightPanel.Visibility = Visibility.Visible;
                    MainGrid.ColumnDefinitions[0].Width = new GridLength(260);
                    MainGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    MainGrid.ColumnDefinitions[2].Width = new GridLength(350);
                    MainGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                    MainGrid.RowDefinitions[1].Height = new GridLength(0);
                    Grid.SetRow(LeftPanel, 0);
                    Grid.SetColumn(LeftPanel, 0);
                    Grid.SetRow(PlayerArea, 0);
                    Grid.SetColumn(PlayerArea, 1);
                }
                else if (width >= 850)
                {
                    LeftPanel.Visibility = Visibility.Visible;
                    RightPanel.Visibility = Visibility.Collapsed;
                    MainGrid.ColumnDefinitions[0].Width = new GridLength(260);
                    MainGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    MainGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                    MainGrid.RowDefinitions[1].Height = new GridLength(0);
                    Grid.SetRow(LeftPanel, 0);
                    Grid.SetColumn(LeftPanel, 0);
                    Grid.SetRow(PlayerArea, 0);
                    Grid.SetColumn(PlayerArea, 1);
                }
                else
                {
                    // Stacked: player on top, channel browser below.
                    LeftPanel.Visibility = Visibility.Visible;
                    RightPanel.Visibility = Visibility.Collapsed;
                    MainGrid.ColumnDefinitions[0].Width = new GridLength(0);
                    MainGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
                    MainGrid.RowDefinitions[0].Height = new GridLength(380);
                    MainGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                    Grid.SetRow(PlayerArea, 0);
                    Grid.SetColumn(PlayerArea, 1);
                    Grid.SetRow(LeftPanel, 1);
                    Grid.SetColumn(LeftPanel, 1);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ApplyLayoutMode");
            }
        }

        // ------------------------------------------------------------ snapshot

        private async void OnSnapshotClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_player == null || _currentChannel == null)
                {
                    ShowSnapshotBar(InfoBarSeverity.Informational,
                        "Start playing a channel first, then take a snapshot.");
                    return;
                }

                var path = await SnapshotService.CaptureAsync(_player, _currentChannel.Name);
                _lastSnapshotDir = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
                ShowSnapshotBar(InfoBarSeverity.Success, $"Snapshot saved: {System.IO.Path.GetFileName(path)}");
                AppLogger.LogAction("SnapshotSaved", new Dictionary<string, object> { { "path", path } });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnSnapshotClick");
                ShowSnapshotBar(InfoBarSeverity.Error, $"Snapshot failed: {ex.Message}");
            }
        }

        private void ShowSnapshotBar(InfoBarSeverity severity, string message)
        {
            SnapshotBar.Severity = severity;
            SnapshotBar.Message = message;
            SnapshotBar.IsOpen = true;
        }

        private async void OnOpenSnapshotsFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = _lastSnapshotDir;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        "TiviIPTVPlayer");
                    System.IO.Directory.CreateDirectory(dir);
                }

                await Launcher.LaunchFolderPathAsync(dir);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "OnOpenSnapshotsFolder");
            }
        }

        // ----------------------------------------------------------------- EPG

        private void UpdateEpgUi()
        {
            try
            {
                if (_currentChannel == null)
                {
                    EpgLine.Text = string.Empty;
                    return;
                }

                var epg = _playlists.ActiveEpg;
                var (current, next) = epg?.GetCurrentAndNext(_currentChannel.TvgId) ?? (null, null);

                if (current != null)
                {
                    EpgLine.Text = next != null
                        ? $"Now: {current.Title} ({current.TimeRangeLocal})   ·   Next: {next.Title} ({next.TimeRangeLocal})"
                        : $"Now: {current.Title} ({current.TimeRangeLocal})";

                    NowCurrentText.Text = $"{current.Title}\n{current.TimeRangeLocal}";
                }
                else
                {
                    EpgLine.Text = epg == null || epg.ChannelCount == 0
                        ? string.IsNullOrEmpty(_playlists.ActiveSource?.EpgUrl)
                            ? "This playlist does not provide an EPG guide"
                            : string.Empty
                        : "No program information available for this channel";
                    NowCurrentText.Text = EpgLine.Text;
                }

                NowNextText.Text = next != null ? $"{next.Title}\n{next.TimeRangeLocal}" : "—";
                NowDescText.Text = current != null && !string.IsNullOrWhiteSpace(current.Description)
                    ? current.Description
                    : string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "UpdateEpgUi");
            }
        }

        private async void OnScheduleClick(object sender, RoutedEventArgs e)
        {
            var channel = _currentChannel ?? ChannelsList.SelectedItem as IptvChannel;
            if (channel != null)
            {
                await ShowChannelDetailsAsync(channel);
            }
        }

        private async void OnDetailsClick(object sender, RoutedEventArgs e)
        {
            var channel = _currentChannel ?? ChannelsList.SelectedItem as IptvChannel;
            if (channel != null)
            {
                await ShowChannelDetailsAsync(channel);
            }
        }

        private async Task ShowChannelDetailsAsync(IptvChannel channel)
        {
            try
            {
                _detailsChannel = channel;
                ChannelDetailsDialog.XamlRoot = XamlRoot;

                DetailsLogo.Source = ChannelConverters.Logo(channel.TvgLogo);
                DetailsName.Text = channel.Name;
                DetailsGroup.Text = string.IsNullOrWhiteSpace(channel.Group) ? "Uncategorized" : channel.Group;

                var epg = _playlists.ActiveEpg;
                var (current, next) = epg?.GetCurrentAndNext(channel.TvgId) ?? (null, null);
                DetailsNowTitle.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
                DetailsNowText.Visibility = current != null ? Visibility.Visible : Visibility.Collapsed;
                DetailsNextTitle.Visibility = next != null ? Visibility.Visible : Visibility.Collapsed;
                DetailsNextText.Visibility = next != null ? Visibility.Visible : Visibility.Collapsed;
                if (current != null)
                {
                    DetailsNowText.Text = $"{current.Title}\n{current.TimeRangeLocal}";
                }
                if (next != null)
                {
                    DetailsNextText.Text = $"{next.Title}\n{next.TimeRangeLocal}";
                }

                var desc = current?.Description ?? string.Empty;
                DetailsDesc.Visibility = string.IsNullOrWhiteSpace(desc) ? Visibility.Collapsed : Visibility.Visible;
                DetailsDesc.Text = desc;

                DetailsScheduleList.Children.Clear();
                var schedule = epg?.GetSchedule(channel.TvgId);
                if (schedule == null || schedule.Count == 0)
                {
                    var none = new TextBlock
                    {
                        Text = string.IsNullOrEmpty(_playlists.ActiveSource?.EpgUrl)
                            ? "This playlist does not provide an EPG guide."
                            : "No program information available for this channel.",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.75,
                    };
                    DetailsScheduleList.Children.Add(none);
                }
                else
                {
                    foreach (var programme in schedule.Take(14))
                    {
                        var line = new TextBlock
                        {
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap,
                        };
                        var time = new Run
                        {
                            Text = programme.TimeRangeLocal + "   ",
                            Foreground = (Brush)Application.Current.Resources["PanelHeaderTextBrush"],
                        };
                        var title = new Run { Text = programme.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                        line.Inlines.Add(time);
                        line.Inlines.Add(title);
                        DetailsScheduleList.Children.Add(line);
                    }
                }

                await ChannelDetailsDialog.ShowQueuedAsync();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ShowChannelDetailsAsync");
            }
        }

        private void OnDetailsWatchClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_detailsChannel != null)
            {
                PlayChannel(_detailsChannel);
            }
        }

        private void OnCopyUrlClick(object sender, RoutedEventArgs e)
        {
            if (_detailsChannel != null)
            {
                CopyToClipboard(_detailsChannel.Url);
                CopyUrlText.Text = "Copied!";
                var unused = Task.Delay(1500).ContinueWith(_ =>
                {
                    DispatcherQueue.TryEnqueue(() => CopyUrlText.Text = "Copy stream URL");
                });
            }
        }

        private static void CopyToClipboard(string text)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "CopyToClipboard");
            }
        }

        private async Task ShowErrorDialogAsync(string title, string message)
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
                AppLogger.LogException(ex, "ShowErrorDialogAsync");
            }
        }
    }
}
