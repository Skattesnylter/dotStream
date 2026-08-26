using System.Windows.Media.Imaging;

namespace DotStream.Icons;

/// <summary>
/// Something launchable: an entry from shell:AppsFolder - a Win32 program, a
/// Store/MSIX package or a shortcut - or a Steam game, which the shell cannot see
/// at all. Launching is identical for all of them, which is the whole reason for
/// going through AppsFolder instead of hunting for .exe files.
/// </summary>
public sealed record InstalledApp(string Name, string AppUserModelId)
{
    public BitmapSource? Icon { get; init; }

    /// <summary>
    /// How to start it. Defaults to the shell's own launcher, which needs nothing but
    /// the AUMID; sources outside the shell - Steam and its steam://rungameid URIs -
    /// set their own.
    /// </summary>
    public string LaunchUri { get; init; } = @"shell:AppsFolder\" + AppUserModelId;

    /// <summary>
    /// True for packaged (Store/MSIX) apps. Their AUMID carries a "!App"-style
    /// entry-point suffix, whereas Win32 entries are .lnk paths or CLSIDs.
    /// </summary>
    public bool IsPackaged => AppUserModelId.Contains('!', StringComparison.Ordinal);

    /// <summary>
    /// The Steam app id, or null for anything that is not a Steam game.
    /// </summary>
    public string? SteamAppId =>
        AppUserModelId.StartsWith("steam:", StringComparison.Ordinal)
            ? AppUserModelId["steam:".Length..]
            : null;

    public override string ToString() => Name;
}
