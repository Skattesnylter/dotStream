using System.Windows.Media.Imaging;

namespace DotStream.Icons;

/// <summary>
/// One entry from shell:AppsFolder - a Win32 program, a Store/MSIX package or a
/// shortcut. Launching is identical for all of them, which is the whole reason
/// for going through AppsFolder instead of hunting for .exe files.
/// </summary>
public sealed record InstalledApp(string Name, string AppUserModelId)
{
    public BitmapSource? Icon { get; init; }

    public string LaunchUri => @"shell:AppsFolder\" + AppUserModelId;

    /// <summary>
    /// True for packaged (Store/MSIX) apps. Their AUMID carries a "!App"-style
    /// entry-point suffix, whereas Win32 entries are .lnk paths or CLSIDs.
    /// </summary>
    public bool IsPackaged => AppUserModelId.Contains('!', StringComparison.Ordinal);

    public override string ToString() => Name;
}
