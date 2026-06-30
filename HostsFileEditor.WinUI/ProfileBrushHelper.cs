using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HostsFileEditor;

internal static class ProfileBrushHelper
{
    public static Brush GetColorBrush(HostsProfile profile)
    {
        var hex = profile?.Metadata?.Color;
        if (string.IsNullOrWhiteSpace(hex)) return new SolidColorBrush(Colors.Transparent);

        hex = hex.TrimStart('#');
        if (hex.Length != 6) return new SolidColorBrush(Colors.Transparent);

        try
        {
            var r = Convert.ToByte(hex[0..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return new SolidColorBrush(Color.FromArgb(255, r, g, b));
        }
        catch (FormatException)
        {
            return new SolidColorBrush(Colors.Transparent);
        }
    }
}
