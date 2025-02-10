using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Wox.Plugin.Logger;
using System.Runtime.InteropServices;
using Community.PowerToys.Run.Plugin.MyGOPowerToys.Properties;

#nullable enable
namespace Community.PowerToys.Run.Plugin.MyGOPowerToys
{
    // Data classes for JSON deserialization
    public class ImageData
    {
        public string url { get; set; } = "";
        public string alt { get; set; } = "";
    }

    public class MyGoResponse
    {
        public List<ImageData> urls { get; set; } = new List<ImageData>();
    }

    public class Main : IPlugin, IContextMenu, ISettingProvider, IDisposable
    {
        private PluginInitContext? _context;
        public string? IconTheme { get; set; }
        public static string PluginID => "0161455A0FDB4D57898EAB503621DBBC";
        public string Name => Resources.name;
        public string Description => Resources.description;
        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>();

        // Cache configuration
        private const string CacheFileName = "cache.json";
        private readonly TimeSpan CacheDuration = TimeSpan.FromHours(2);
        private List<ImageData> cachedImages = new List<ImageData>();

        public void Init(PluginInitContext context)
        {
            _context = context;
            _context.API.ThemeChanged += OnThemeChanged;
            UpdateIconTheme(_context.API.GetCurrentTheme());
        }

        private void UpdateIconTheme(Theme theme) =>
            IconTheme = theme == Theme.Light || theme == Theme.HighContrastWhite
                        ? _context?.CurrentPluginMetadata.IcoPathLight
                        : _context?.CurrentPluginMetadata.IcoPathDark;

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconTheme(newTheme);

        // Returns a cache file path within LocalApplicationData
        private string GetCachePath()
        {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGOPowerToys");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return System.IO.Path.Combine(folder, CacheFileName);
        }

        /// <summary>
        /// Loads the cached JSON data from file.
        /// If the file does not exist or is older than 2 hours, the cache is refreshed.
        /// </summary>
        private void LoadCache()
        {
            try
            {
                string cachePath = GetCachePath();
                bool refresh = true;
                if (File.Exists(cachePath))
                {
                    DateTime lastWrite = File.GetLastWriteTime(cachePath);
                    if (DateTime.Now - lastWrite < CacheDuration)
                    {
                        refresh = false;
                    }
                }
                if (refresh)
                {
                    RefreshCache(cachePath);
                }
                string json = File.ReadAllText(cachePath);
                var response = JsonSerializer.Deserialize<MyGoResponse>(json);
                cachedImages = response?.urls ?? new List<ImageData>();
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading cache.\n{ex.Message}", GetType());
            }
        }

        /// <summary>
        /// Refreshes the cache file by sending a HTTP GET to the MyGO API.
        /// </summary>
        private void RefreshCache(string cachePath)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var json = client.GetStringAsync("https://mygoapi.miyago9267.com/mygo/all_img")
                                     .GetAwaiter().GetResult();
                    File.WriteAllText(cachePath, json);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error refreshing cache.\n{ex.Message}", GetType());
            }
        }

        /// <summary>
        /// When the user types a query, filter the cached images by checking if the image¡¦s alt contains the search text.
        /// Pagination functionality has been removed.
        /// </summary>
        public List<Result> Query(Query query)
        {
            Log.Info("Query: " + query.Search, GetType());
            string searchText = query.Search;

            LoadCache(); // ensure cache is current

            // Filter images whose 'alt' contains the search text (case-insensitive)
            var filtered = cachedImages
                .Where(img => img.alt.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var results = new List<Result>();

            // Add each matching image as a result.
            // The action downloads the image from its URL and copies it to the clipboard.
            foreach (var img in filtered)
            {
                results.Add(new Result
                {
                    Title = img.alt,
                    SubTitle = img.url,
                    IcoPath = IconTheme,
                    // Store the image URL in ContextData for use in the context menu
                    ContextData = img.url,
                    Action = _ =>
                    {
                        CopyImageToClipboard(img.url);
                        return true;
                    }
                });
            }

            return results;
        }

        /// <summary>
        /// Provides a context menu that allows the user to copy the image URL as text.
        /// </summary>
        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            Log.Info("LoadContextMenus", GetType());
            if (selectedResult?.ContextData is string imageUrl)
            {
                return new List<ContextMenuResult>
                {
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Copy Image URL (Enter)",
                        FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                        Glyph = "\xE8C8", // Copy icon
                        AcceleratorKey = Key.Enter,
                        Action = _ =>
                        {
                            CopyToClipboard(imageUrl);
                            return true;
                        }
                    }
                };
            }
            return new List<ContextMenuResult>();
        }

        // Return a basic UserControl to satisfy ISettingProvider
        public Control CreateSettingPanel() => new UserControl();

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            Log.Info("UpdateSettings", GetType());
            // No additional settings to update for this plugin
        }

        public void Dispose()
        {
            Log.Info("Dispose", GetType());
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_context?.API != null)
                {
                    _context.API.ThemeChanged -= OnThemeChanged;
                }
            }
        }

        /// <summary>
        /// Downloads the image from the provided URL and copies it to the clipboard.
        /// </summary>
        private static bool CopyImageToClipboard(string imageUrl)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var bytes = client.GetByteArrayAsync(imageUrl).GetAwaiter().GetResult();
                    using (var ms = new MemoryStream(bytes))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = ms;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        Clipboard.SetImage(bitmap);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error copying image to clipboard.\n{ex.Message}", typeof(Main));
                return false;
            }
        }

        /// <summary>
        /// Copies the provided text to the clipboard.
        /// </summary>
        private static bool CopyToClipboard(string? value)
        {
            if (value == null) return false;
            const uint CLIPBRD_E_CANT_OPEN = 0x800401D0;
            const int MAX_RETRIES = 5;
            const int SLEEP_TIME_MS = 5;

            for (int i = 0; i < MAX_RETRIES; i++)
            {
                try
                {
                    Clipboard.SetDataObject(value, true);
                    return true;
                }
                catch (COMException clipboardException) when ((uint)clipboardException.ErrorCode == CLIPBRD_E_CANT_OPEN)
                {
                    Thread.Sleep(SLEEP_TIME_MS);
                }
            }
            return false;
        }
    }
}
