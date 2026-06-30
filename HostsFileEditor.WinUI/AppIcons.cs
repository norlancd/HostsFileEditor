using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HostsFileEditor;

/// <summary>
/// Centralises every Segoe Fluent Icons glyph used by menus and toolbars.
/// No two unrelated functions share the same constant.
/// </summary>
internal static class AppIcons
{
    public const string FontFamilyName = "Segoe Fluent Icons";

    // -- File -----------------------------------------------------------------
    public const string Export               = ""; // SaveCopy  - export profiles
    public const string Import               = ""; // Import    - import profiles / host file
    public const string SaveCurrentAsProfile = ""; // Attach    - save current state as new profile
    public const string OpenTextEditor       = ""; // Pencil    - open in text editor
    public const string RestoreDefault       = ""; // Restore
    public const string Exit                 = ""; // Leave

    // -- Edit -----------------------------------------------------------------
    public const string Refresh              = ""; // Sync arrows
    public const string Duplicate            = ""; // AddTo - doc+plus, clearly "add another of this"
    public const string MoveUp               = ""; // ChevronUp
    public const string MoveDown             = ""; // ChevronDown
    public const string InsertAbove          = ""; // Insert row above
    public const string InsertBelow          = ""; // Insert row below
    public const string EnableDisable        = ""; // Toggle

    // -- Profiles -------------------------------------------------------------
    public const string NewProfile           = ""; // Add circle - new empty profile
    public const string Activate             = ""; // Play
    public const string Reload                = ""; // RepeatAll - re-apply active profile
    public const string ActivateTimer        = ""; // Timer
    public const string Clone                = ""; // AddTo      - distinct from Duplicate
    public const string RawEdit              = ""; // Pencil
    public const string ProfileSettings      = ""; // Gear
    public const string Delete               = ""; // Trash
    public const string HostsDisabled        = ""; // Block/Forbidden
    public const string ProfilesPanel        = ""; // Library
    public const string ActiveProfileCheck   = ""; // Checkmark (active indicator only)

    // -- View -----------------------------------------------------------------
    public const string HideComments         = ""; // Eye/hide comments
    public const string HideDisabled         = ""; // Eye/disabled entries
    public const string ResetFilters         = ""; // Clear/Erase - distinct from Refresh
    public const string AuditLog             = ""; // List/Log

    // -- Settings -------------------------------------------------------------
    public const string PingIPs              = ""; // Network signal
    public const string RemoveDefaultText    = ""; // Clear text
    public const string Diff                 = ""; // Compare
    public const string Theme                = ""; // Brightness/Sun - theme selector

    // -- Help -----------------------------------------------------------------
    public const string About                = ""; // Info circle

    // -- Factory --------------------------------------------------------------
    public static FontIcon Create(string glyph) => new()
    {
        FontFamily = new FontFamily(FontFamilyName),
        Glyph      = glyph,
    };
}