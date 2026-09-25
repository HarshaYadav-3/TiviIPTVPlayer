using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TiviIPTVPlayer.Services
{
    /// <summary>
    /// Serializes ContentDialog display app-wide. WinUI 3 throws
    /// "Only a single ContentDialog can be open at any time" when ShowAsync
    /// races another open dialog; every dialog in the app goes through here.
    /// </summary>
    internal static class DialogQueue
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static async Task<ContentDialogResult> ShowQueuedAsync(this ContentDialog dialog)
        {
            await Gate.WaitAsync();
            try
            {
                return await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"DialogQueue.ShowQueuedAsync({dialog.Title})");
                return ContentDialogResult.None;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
