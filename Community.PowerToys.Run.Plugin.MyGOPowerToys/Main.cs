using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Wox.Plugin.Logger;
using System.Runtime.InteropServices;
using System.Runtime.Versioning; // Needed for SupportedOSPlatform attribute
using Community.PowerToys.Run.Plugin.MyGOPowerToys.Properties;

[assembly: SupportedOSPlatform("windows")]

#nullable enable
namespace Community.PowerToys.Run.Plugin.MyGOPowerToys
{
    [SupportedOSPlatform("windows")]
    public class ImageData
    {
        public string url { get; set; } = "";
        public string alt { get; set; } = "";
        public string AltLower { get; set; } = "";
    }

    public class MyGoResponse
    {
        public List<ImageData> urls { get; set; } = new List<ImageData>();
    }

    [SupportedOSPlatform("windows")]
    public class Main : IPlugin, IContextMenu, ISettingProvider, IDisposable
    {
        private PluginInitContext? _context;
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly object _cacheLock = new object();
        private List<ImageData> _cachedImages = new List<ImageData>();
        private DateTime _lastCacheUpdate = DateTime.MinValue;

        public string? IconTheme { get; set; }
        public static string PluginID => "0161455A0FDB4D57898EAB503621DBBC";
        public string Name => Resources.name;
        public string Description => Resources.description;
        public IEnumerable<PluginAdditionalOption> AdditionalOptions => Array.Empty<PluginAdditionalOption>();

        private const string CacheFileName = "cache.json";
        private readonly TimeSpan CacheDuration = TimeSpan.FromHours(2);
        private const int MaxNetworkRetries = 3;
        private const int RetryDelayMs = 300;

        public void Init(PluginInitContext context)
        {
            _context = context;
            _context.API.ThemeChanged += OnThemeChanged;
            UpdateIconTheme(_context.API.GetCurrentTheme());
            _ = InitializeCacheAsync();
        }

        private void UpdateIconTheme(Theme theme)
        {
            IconTheme = (theme == Theme.Light || theme == Theme.HighContrastWhite)
                ? _context?.CurrentPluginMetadata.IcoPathLight
                : _context?.CurrentPluginMetadata.IcoPathDark;
        }

        private void OnThemeChanged(Theme oldTheme, Theme newTheme) => UpdateIconTheme(newTheme);

        public Control CreateSettingPanel() => new UserControl();

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            // No settings to update
        }

        public void Dispose()
        {
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
                // Do not dispose the static HttpClient
            }
        }

        private async Task InitializeCacheAsync()
        {
            try
            {
                var cachePath = GetCachePath();
                if (File.Exists(cachePath))
                {
                    await LoadCacheFromFileAsync(cachePath);
                }
                _ = RefreshCacheAsync(force: true);
            }
            catch (Exception ex)
            {
                Log.Error($"Initialization error: {ex.Message}", GetType());
            }
        }

        private string GetCachePath()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGOPowerToys");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, CacheFileName);
        }

        private async Task RefreshCacheAsync(bool force = false)
        {
            var cachePath = GetCachePath();
            if (!force && DateTime.Now - _lastCacheUpdate <= CacheDuration)
                return;

            for (int i = 0; i < MaxNetworkRetries; i++)
            {
                try
                {
                    var json = await _httpClient.GetStringAsync("https://mygoapi.miyago9267.com/mygo/all_img");
                    await File.WriteAllTextAsync(cachePath, json);
                    await LoadCacheFromFileAsync(cachePath);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == MaxNetworkRetries - 1)
                        Log.Error($"Cache refresh failed: {ex.Message}", GetType());

                    await Task.Delay(RetryDelayMs);
                }
            }
        }

        private async Task LoadCacheFromFileAsync(string cachePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(cachePath);
                var response = JsonSerializer.Deserialize<MyGoResponse>(json);
                lock (_cacheLock)
                {
                    _cachedImages = response?.urls
                        .Select(img => new ImageData
                        {
                            url = img.url,
                            alt = img.alt,
                            AltLower = img.alt.ToLowerInvariant()
                        })
                        .ToList() ?? new List<ImageData>();
                    _lastCacheUpdate = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Cache load error: {ex.Message}", GetType());
            }
        }

        public List<Result> Query(Query query)
        {
            var searchText = query.Search.ToLowerInvariant();
            var results = new List<Result>();

            lock (_cacheLock)
            {
                foreach (var img in _cachedImages.Where(img => img.AltLower.Contains(searchText)))
                {
                    results.Add(new Result
                    {
                        Title = img.alt,
                        SubTitle = img.url,
                        IcoPath = IconTheme,
                        ContextData = img.url,
                        Action = _ => CopyImageToClipboard(img.url)
                    });
                }
            }

            _ = RefreshCacheAsync(); // Background refresh
            return results;
        }

        private bool CopyImageToClipboard(string url)
        {
            for (int i = 0; i < MaxNetworkRetries; i++)
            {
                try
                {
                    using var stream = _httpClient.GetStreamAsync(url).GetAwaiter().GetResult();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Clipboard.SetImage(bitmap);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error($"Image copy failed ({i + 1}/{MaxNetworkRetries}): {ex.Message}", GetType());
                    Thread.Sleep(RetryDelayMs);
                }
            }
            return false;
        }

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult?.ContextData is string url)
            {
                return new List<ContextMenuResult>
                {
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Copy Image URL (Enter)",
                        FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                        Glyph = "\xE8C8",
                        AcceleratorKey = Key.Enter,
                        Action = _ => CopyToClipboard(url)
                    }
                };
            }
            return new List<ContextMenuResult>();
        }

        private bool CopyToClipboard(string? text)
        {
            const uint CLIPBRD_E_CANT_OPEN = 0x800401D0;
            const int MaxAttempts = 5;

            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < MaxAttempts; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (COMException ex) when ((uint)ex.ErrorCode == CLIPBRD_E_CANT_OPEN)
                {
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    Log.Error($"Clipboard error: {ex.Message}", GetType());
                    break;
                }
            }
            return false;
        }
    }
}
