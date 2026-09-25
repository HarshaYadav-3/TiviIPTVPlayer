using Microsoft.Win32;
using System;
using System.IO;

namespace TiviIPTVPlayer.Services
{
    /// <summary>
    /// Registry-based Run-on-Startup (Auto-Start) via HKCU ...\Run.
    /// The value is written as "&lt;exePath&gt; --startup".
    /// </summary>
    public class StartupManager
    {
        private const string RunKeyName = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly string _appName;

        /// <param name="appName">
        /// Unique registry value name for this app. Defaults to the exe name.
        /// </param>
        public StartupManager(string? appName = null)
        {
            _appName = string.IsNullOrWhiteSpace(appName)
                ? ResolveDefaultAppName()
                : appName!;
        }

        /// <summary>Registry value name used for this app's startup entry.</summary>
        public string AppName => _appName;

        /// <summary>Checks if the startup Registry key exists and is non-empty.</summary>
        public bool IsAutoStartEnabled
        {
            get
            {
                try
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyName, false);
                    if (key != null)
                    {
                        string? value = key.GetValue(_appName) as string;
                        return !string.IsNullOrEmpty(value);
                    }
                }
                catch (Exception) { }
                return false;
            }
        }

        /// <summary>Registers or unregisters the app executable path with the --startup argument.</summary>
        public void SetAutoStart(bool enable)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyName, true);
                if (key != null)
                {
                    if (enable)
                    {
                        string exePath = GetExecutablePath();
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(_appName, $"\"{exePath}\" --startup");
                        }
                    }
                    else
                    {
                        key.DeleteValue(_appName, false);
                    }
                }
            }
            catch (Exception) { }
        }

        private string GetExecutablePath()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    return System.Reflection.Assembly.GetExecutingAssembly().Location;
                return Path.GetFullPath(exePath);
            }
            catch (Exception) { }
            return string.Empty;
        }

        private static string ResolveDefaultAppName()
        {
            try
            {
                string? exe = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? null);
                if (!string.IsNullOrWhiteSpace(exe))
                    return exe!;
            }
            catch (Exception) { }
            return "TiviIPTVPlayer";
        }
    }
}
