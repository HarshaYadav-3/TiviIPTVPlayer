using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI.ViewManagement;

namespace TiviIPTVPlayer.Services
{
    /// <summary>
    /// Central theme helper for the blank-app template.
    /// "System" (ElementTheme.Default on the root) follows the current Windows
    /// app theme so XAML ThemeResources switch automatically. Code-behind must
    /// resolve via <see cref="GetEffectiveTheme"/> so C# brushes stay in sync
    /// when Windows switches light/dark at runtime.
    /// </summary>
    public static class ThemeHelper
    {
        private const string ThemeSettingsKey = "AppTheme";

        /// <summary>
        /// Determines the current Windows app theme. Never returns Default.
        /// Order: WinUI Application.RequestedTheme -&gt; UISettings background
        /// brightness -&gt; registry AppsUseLightTheme -&gt; Dark fallback.
        /// </summary>
        public static ElementTheme GetSystemTheme()
        {
            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    if (app.RequestedTheme == ApplicationTheme.Light)
                    {
                        return ElementTheme.Light;
                    }
                    if (app.RequestedTheme == ApplicationTheme.Dark)
                    {
                        return ElementTheme.Dark;
                    }
                }
            }
            catch
            {
            }

            try
            {
                var uiSettings = new UISettings();
                var bg = uiSettings.GetColorValue(UIColorType.Background);
                var luminance = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
                return luminance < 128 ? ElementTheme.Dark : ElementTheme.Light;
            }
            catch
            {
            }

            try
            {
                var value = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1);
                if (value is int i)
                {
                    return i == 0 ? ElementTheme.Dark : ElementTheme.Light;
                }
            }
            catch
            {
            }

            return ElementTheme.Dark;
        }

        /// <summary>
        /// Resolves the effective (real Light/Dark) theme for an element.
        /// Follows the RequestedTheme chain; Default falls back to the OS theme.
        /// </summary>
        public static ElementTheme GetEffectiveTheme(FrameworkElement? element)
        {
            try
            {
                var current = element;
                while (current != null)
                {
                    if (current.RequestedTheme == ElementTheme.Light)
                    {
                        return ElementTheme.Light;
                    }
                    if (current.RequestedTheme == ElementTheme.Dark)
                    {
                        return ElementTheme.Dark;
                    }
                    current = current.Parent as FrameworkElement;
                }

                var requested = element?.RequestedTheme ?? ElementTheme.Default;
                if (requested == ElementTheme.Light || requested == ElementTheme.Dark)
                {
                    return requested;
                }
            }
            catch
            {
            }
            return GetSystemTheme();
        }

        public static bool IsDark(ElementTheme effective) => effective != ElementTheme.Light;

        public static bool IsDark(FrameworkElement? element) => IsDark(GetEffectiveTheme(element));

        private static ResourceDictionary? GetThemeDictionary(ElementTheme effective)
        {
            try
            {
                var app = Application.Current;
                if (app?.Resources?.ThemeDictionaries == null)
                {
                    return null;
                }
                var key = effective == ElementTheme.Light ? "Light" : "Dark";
                if (app.Resources.ThemeDictionaries.TryGetValue(key, out var obj) && obj is ResourceDictionary dict)
                {
                    return dict;
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// Theme-aware brush lookup. Falls back to Application.Resources for
        /// non-theme keys.
        /// </summary>
        public static SolidColorBrush? ResolveBrush(string key, FrameworkElement? scope)
            => ResolveBrush(key, GetEffectiveTheme(scope));

        public static SolidColorBrush? ResolveBrush(string key, ElementTheme effective)
        {
            try
            {
                var dict = GetThemeDictionary(effective);
                if (dict != null && dict.TryGetValue(key, out var themed) && themed is SolidColorBrush tb)
                {
                    return tb;
                }
                var app = Application.Current;
                if (app?.Resources != null && app.Resources.TryGetValue(key, out var fallback) && fallback is SolidColorBrush fb)
                {
                    return fb;
                }
            }
            catch
            {
            }
            return null;
        }

        public static Windows.UI.Color ResolveColor(string brushKey, FrameworkElement? scope, Windows.UI.Color fallback)
        {
            try
            {
                var brush = ResolveBrush(brushKey, scope);
                if (brush != null)
                {
                    return brush.Color;
                }
            }
            catch
            {
            }
            return fallback;
        }

        // ---- Persistence (LocalSettings) so the toggle survives restarts ----

        public static ElementTheme LoadSavedTheme()
        {
            try
            {
                var s = SettingsService.GetString(ThemeSettingsKey);
                if (!string.IsNullOrEmpty(s))
                {
                    if (string.Equals(s, "Light", StringComparison.OrdinalIgnoreCase))
                    {
                        return ElementTheme.Light;
                    }
                    if (string.Equals(s, "Dark", StringComparison.OrdinalIgnoreCase))
                    {
                        return ElementTheme.Dark;
                    }
                }
            }
            catch
            {
            }
            return ElementTheme.Default;
        }

        public static void SaveTheme(ElementTheme theme)
        {
            try
            {
                SettingsService.SetString(ThemeSettingsKey, theme switch
                {
                    ElementTheme.Light => "Light",
                    ElementTheme.Dark => "Dark",
                    _ => "System",
                });
            }
            catch
            {
            }
        }

        public static ElementTheme NextTheme(ElementTheme current) => current switch
        {
            ElementTheme.Default => ElementTheme.Light,
            ElementTheme.Light => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
