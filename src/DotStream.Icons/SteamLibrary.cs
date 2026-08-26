using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DotStream.Icons;

/// <summary>
/// Steam games, which <see cref="AppsFolder"/> cannot see.
///
/// Steam stopped writing Start-menu entries, so an installed game exists nowhere the
/// shell looks: measured on a real library, thirty-five games were installed and none
/// of them appeared in shell:AppsFolder. The only trace on disk is Steam's own
/// bookkeeping, which is what this reads.
///
/// Nothing here touches the network. Steam already downloads artwork for the games you
/// browse, and this uses that cache; a game whose artwork has never been fetched falls
/// back to the 32x32 client icon, which is small but always present.
/// </summary>
public static class SteamLibrary
{
    /// <summary>
    /// Tools and runtimes that live in the library alongside games. They are installed
    /// the same way and would otherwise show up as launchable, which they are not.
    /// </summary>
    private static readonly HashSet<string> NotGames =
    [
        "228980", // Steamworks Common Redistributables
        "1070560", // Steam Linux Runtime 1.0
        "1391110", // Steam Linux Runtime 2.0
        "1628350", // Steam Linux Runtime 3.0
        "1493710", // Proton Experimental
    ];

    /// <summary>Where Steam is installed, or null if it is not.</summary>
    public static string? InstallPath
    {
        get
        {
            // The per-user key is the one Steam keeps current; the machine-wide key is
            // written by the installer and can point at an older location.
            object? value = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null)
                         ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null);

            string? path = value as string;
            if (string.IsNullOrWhiteSpace(path)) return null;

            // The per-user key stores forward slashes and lower case.
            path = path.Replace('/', '\\');
            return Directory.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Every installed game, as launchable entries. Empty when Steam is not installed,
    /// so callers can concatenate unconditionally.
    /// </summary>
    public static IReadOnlyList<InstalledApp> Enumerate(bool loadIcons = true)
    {
        var results = new List<InstalledApp>();

        string? steam = InstallPath;
        if (steam is null) return results;

        string cache = Path.Combine(steam, "appcache", "librarycache");
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string library in LibraryFolders(steam))
        {
            string apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;

            string[] manifests;
            try { manifests = Directory.GetFiles(apps, "appmanifest_*.acf"); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (string manifest in manifests)
            {
                (string? id, string? name) = ReadManifest(manifest);

                if (id is null || name is null) continue;
                if (NotGames.Contains(id)) continue;
                if (!seen.Add(id)) continue; // the same game can appear in two libraries

                results.Add(new InstalledApp(name, "steam:" + id)
                {
                    Icon = loadIcons ? LoadArtwork(cache, id) : null,
                    LaunchUri = "steam://rungameid/" + id,
                });
            }
        }

        results.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return results;
    }

    /// <summary>
    /// Whether Steam currently has this game running.
    ///
    /// A game gives no useful answer to the usual process questions - it is started by
    /// Steam, often under a launcher, and its window may belong to neither. Steam keeps
    /// a Running flag per app instead, which is both cheaper to read and correct.
    /// </summary>
    public static bool IsRunning(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return false;

        object? running = Registry.GetValue(
            @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam\Apps\" + appId, "Running", null);

        return running is int flag && flag != 0;
    }

    /// <summary>
    /// The library roots. Games do not all live under the Steam install - a second disk
    /// is the normal case, and libraryfolders.vdf is the only list of them.
    /// </summary>
    private static List<string> LibraryFolders(string steam)
    {
        var folders = new List<string> { steam };

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return folders;

        string text;
        try { text = File.ReadAllText(vdf); }
        catch (IOException) { return folders; }

        foreach (Match match in Regex.Matches(text, @"""path""\s+""(.+?)"""))
        {
            // VDF escapes backslashes.
            string path = match.Groups[1].Value.Replace(@"\\", @"\");

            if (Directory.Exists(path) &&
                !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(path);
            }
        }

        return folders;
    }

    private static (string? Id, string? Name) ReadManifest(string path)
    {
        string text;

        try { text = File.ReadAllText(path); }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }

        Match id = Regex.Match(text, @"""appid""\s+""(\d+)""");
        Match name = Regex.Match(text, @"""name""\s+""(.*?)""");

        if (!id.Success || !name.Success) return (null, null);

        return (id.Groups[1].Value, name.Groups[1].Value);
    }

    /// <summary>
    /// The best square picture Steam has already cached for a game.
    ///
    /// Preference is by usable resolution on a 100x100 cell, not by what the file is
    /// called. The portrait capsule is 300x450 and crops to a clean square; the header
    /// is 460x215 and crops to a passable one; the client icon is 32x32 and is what
    /// remains for a game whose artwork has never been fetched. Measured on a real
    /// library: eighteen of thirty-two games had the good artwork, all thirty-two had
    /// the icon. Browsing the library in Steam once fills in the rest.
    /// </summary>
    private static BitmapSource? LoadArtwork(string cache, string appId)
    {
        string folder = Path.Combine(cache, appId);
        if (!Directory.Exists(folder)) return null;

        // Portrait art is drawn with the title in the lower half, so a square taken from
        // the top would cut it off. Centre is the safe crop for both of these.
        BitmapSource? art = CropSquare(Path.Combine(folder, "library_600x900.jpg"))
                         ?? CropSquare(Path.Combine(folder, "header.jpg"));

        if (art is not null) return art;

        try
        {
            // Whatever hash-named jpg is left is the 32x32 client icon.
            foreach (string file in Directory.GetFiles(folder, "*.jpg"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.Length == 40) return Load(file);
            }
        }
        catch (IOException) { }

        return null;
    }

    private static BitmapSource? CropSquare(string path)
    {
        BitmapSource? source = Load(path);
        if (source is null) return null;

        int side = Math.Min(source.PixelWidth, source.PixelHeight);
        if (side <= 0) return null;

        var crop = new CroppedBitmap(source, new System.Windows.Int32Rect(
            (source.PixelWidth - side) / 2,
            (source.PixelHeight - side) / 2,
            side, side));

        crop.Freeze();
        return crop;
    }

    private static BitmapSource? Load(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            // Steam rewrites this cache while it runs, and an open file handle would
            // make it fail. OnLoad reads the bytes and lets go.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
