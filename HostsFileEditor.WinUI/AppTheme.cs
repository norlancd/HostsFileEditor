using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;

namespace HostsFileEditor;

internal enum AppTheme { System, Light, Dark }

internal static class AppThemeHelper
{
    public static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark  => ElementTheme.Dark,
        _              => ElementTheme.Default,
    };

    public static SystemBackdropTheme ToBackdropTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => SystemBackdropTheme.Light,
        AppTheme.Dark  => SystemBackdropTheme.Dark,
        _              => SystemBackdropTheme.Default,
    };

    public static AppTheme FromString(string? s) => s switch
    {
        "Light" => AppTheme.Light,
        "Dark"  => AppTheme.Dark,
        _       => AppTheme.System,
    };
}
