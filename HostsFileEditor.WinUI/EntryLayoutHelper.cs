using Microsoft.UI.Text;   // FontWeights static helpers
using Microsoft.UI.Xaml;
using Windows.UI.Text;     // FontStyle, FontWeight structs

namespace HostsFileEditor;

/// <summary>
/// Static helpers called from the entries DataTemplate via x:Bind.
/// All rows use the same 4-column layout; comment-only rows are visually
/// de-emphasized (dimmed IP/hostname fields, bold italic comment) so the user
/// can still click into any field to "upgrade" the row to a real entry.
/// </summary>
internal static class EntryLayoutHelper
{
    public static Thickness GetPadding(bool hasCommentOnly)
        => hasCommentOnly ? new Thickness(8, 2, 8, 2) : new Thickness(8, 4, 8, 4);

    /// <summary>Dims IP and hostname TextBoxes for comment-only rows.</summary>
    public static double GetFieldOpacity(bool hasCommentOnly)
        => hasCommentOnly ? 0.35 : 1.0;

    public static FontStyle GetCommentFontStyle(bool hasCommentOnly)
        => hasCommentOnly ? FontStyle.Italic : FontStyle.Normal;

    public static FontWeight GetCommentFontWeight(bool hasCommentOnly)
        => hasCommentOnly ? FontWeights.SemiBold : FontWeights.Normal;
}
