using Microsoft.Win32;
using System;
using System.Windows;

namespace YouTubeDownloader.Services;

public enum AppTheme
{
    Dark,
    Light,
    Auto
}

public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    public event EventHandler? ThemeChanged;

    private AppTheme _currentThemeSetting = AppTheme.Dark;

    public AppTheme CurrentThemeSetting
    {
        get => _currentThemeSetting;
        set
        {
            _currentThemeSetting = value;
            ApplyTheme();
            SaveThemePreference();
        }
    }

    public bool IsDarkMode { get; private set; }

    public ThemeService()
    {
        LoadThemePreference();
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
    }

    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _currentThemeSetting == AppTheme.Auto)
        {
            Application.Current.Dispatcher.Invoke(ApplyTheme);
        }
    }

    public void ApplyTheme()
    {
        IsDarkMode = _currentThemeSetting switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            AppTheme.Auto => IsSystemDarkMode(),
            _ => true
        };

        var app = Application.Current;
        var themeDictionary = new ResourceDictionary
        {
            Source = new Uri(IsDarkMode
                ? "pack://application:,,,/Themes/DarkTheme.xaml"
                : "pack://application:,,,/Themes/LightTheme.xaml")
        };

        // Remove existing theme dictionary and add new one
        if (app.Resources.MergedDictionaries.Count > 0)
        {
            app.Resources.MergedDictionaries.Clear();
        }
        app.Resources.MergedDictionaries.Add(themeDictionary);

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return true; // Default to dark if we can't read
        }
    }

    private void SaveThemePreference()
    {
        try
        {
            Properties.Settings.Default.Theme = _currentThemeSetting.ToString();
            Properties.Settings.Default.Save();
        }
        catch
        {
            // Ignore save errors
        }
    }

    private void LoadThemePreference()
    {
        try
        {
            var saved = Properties.Settings.Default.Theme;
            if (Enum.TryParse<AppTheme>(saved, out var theme))
            {
                _currentThemeSetting = theme;
            }
        }
        catch
        {
            _currentThemeSetting = AppTheme.Dark;
        }
    }
}
