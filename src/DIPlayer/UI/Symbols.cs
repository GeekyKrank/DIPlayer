using System.Windows.Media;

namespace DIPlayer.UI;

/// <summary>Embedded Flaticon Uicons 4.0, solid rounded. Works without a network connection.</summary>
public static class Symbols
{
    public const string More = Menu;
    public static FontFamily Font { get; } = new(new Uri("pack://application:,,,/DIPlayer;component/Assets/", UriKind.Absolute), "./#uicons-solid-rounded");
    public const string Play = "\uEF70"; // fi-sr-play
    public const string Pause = "\uEE85"; // fi-sr-pause
    public const string Previous = "\uF2F8"; // fi-sr-step-backward
    public const string Next = "\uF2F9"; // fi-sr-step-forward
    public const string Shuffle = "\uF1A8"; // fi-sr-shuffle
    public const string Repeat = "\uE12E"; // fi-sr-arrows-repeat
    public const string RepeatOne = "\uE12F"; // fi-sr-arrows-repeat-1
    public const string Heart = "\uEA5A"; // fi-sr-heart
    public const string Volume = "\uF59D"; // fi-sr-volume
    public const string VolumeMuted = "\uF5A0"; // fi-sr-volume-mute
    public const string Lyrics = "\uE55D"; // fi-sr-comment-alt-music
    public const string Equalizer = "\uF1FC"; // fi-sr-sliders-v
    public const string Queue = "\uEC34"; // fi-sr-list-music
    public const string Home = "\uEA8B"; // fi-sr-home
    public const string Search = "\uF13A"; // fi-sr-search
    public const string Library = "\uE262"; // fi-sr-books
    public const string Settings = "\uF163"; // fi-sr-settings
    public const string Close = "\uE5EB"; // fi-sr-cross-small
    public const string Down = "\uE0A1"; // fi-sr-angle-small-down
    public const string Up = "\uE0A4"; // fi-sr-angle-small-up
    public const string Back = "\uE0A2"; // fi-sr-angle-small-left
    public const string Forward = "\uE0A3"; // fi-sr-angle-small-right
    public const string Plus = "\uEF88"; // fi-sr-plus-small
    public const string Minus = "\uED28"; // fi-sr-minus-small
    public const string Check = "\uE430"; // fi-sr-check
    public const string User = "\uF4F9"; // fi-sr-user
    public const string Clock = "\uE4CB"; // fi-sr-clock
    public const string Download = "\uE70D"; // fi-sr-download
    public const string Music = "\uED9F"; // fi-sr-music-alt
    public const string View = "\uE7BB"; // fi-sr-eye
    public const string SortUp = "\uF24F"; // fi-sr-sort-amount-up
    public const string SortDown = "\uF24D"; // fi-sr-sort-amount-down
    public const string Grid = "\uE9A6"; // fi-sr-grid
    public const string List = "\uEC31"; // fi-sr-list
    public const string Refresh = "\uF05E"; // fi-sr-refresh
    public const string Menu = "\uECEC"; // fi-sr-menu-dots
    public const string MenuVertical = "\uECED"; // fi-sr-menu-dots-vertical
    public const string Minimize = "\uED25"; // fi-sr-minus
    public const string Maximize = "\uF28F"; // fi-sr-square
    public const string Restore = "\uE4E7"; // fi-sr-clone
    public const string FolderAdd = "\uE03B"; // fi-sr-add-folder
    public const string Share = "\uF16A"; // fi-sr-share
    public const string Info = "\uEB10"; // fi-sr-info
    public const string Link = "\uEC21"; // fi-sr-link
    public const string Bookmark = "\uE260"; // fi-sr-bookmark
    public const string External = "\uE11D"; // fi-sr-arrow-up-right-from-square
    public const string Album = "\uF03D"; // fi-sr-record-vinyl
    public const string Edit = "\uE759"; // fi-sr-edit
    public const string Trash = "\uF463"; // fi-sr-trash
    public const string Import = "\uE84F"; // fi-sr-file-import
}
