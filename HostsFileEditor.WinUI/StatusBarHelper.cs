using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HostsFileEditor;

internal static class StatusBarHelper
{
    public enum DotColor { Green, Yellow, Red, Gray }

    public static Brush Dot(DotColor color) => new SolidColorBrush(color switch
    {
        DotColor.Green  => Color.FromArgb(0xFF, 0x0F, 0x7B, 0x0F),
        DotColor.Yellow => Color.FromArgb(0xFF, 0xD8, 0x8A, 0x00),
        DotColor.Red    => Color.FromArgb(0xFF, 0xC4, 0x26, 0x00),
        _               => Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E),
    });
}
