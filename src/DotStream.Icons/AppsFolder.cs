using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace DotStream.Icons;

/// <summary>
/// Enumerates everything Windows considers a launchable application, with the
/// same icons Explorer itself uses.
///
/// Why not Icon.ExtractAssociatedIcon: it caps out at 32x32, which looks awful
/// scaled to an 85x85 LCD, and it finds nothing at all for Store/MSIX apps such
/// as Spotify or the new Teams. IShellItemImageFactory handles both, with real
/// 32-bit alpha, at whatever size we ask for.
///
/// Shell COM wants an STA thread. Call <see cref="EnumerateAsync"/> unless you
/// already know you are on one.
/// </summary>
public static class AppsFolder
{
    /// <summary>
    /// Enumerates installed applications on a dedicated STA thread. Icons are
    /// frozen, so the result can be handed to any thread.
    /// </summary>
    public static Task<IReadOnlyList<InstalledApp>> EnumerateAsync(int iconSize = 256, bool loadIcons = true)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<InstalledApp>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Enumerate(iconSize, loadIcons));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "dotStream AppsFolder"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    /// <summary>Synchronous enumeration. Must be called on an STA thread.</summary>
    public static IReadOnlyList<InstalledApp> Enumerate(int iconSize = 256, bool loadIcons = true)
    {
        var results = new List<InstalledApp>();

        int hr = ShellInterop.SHGetKnownFolderItem(
            ShellInterop.FolderIdAppsFolder, 0, IntPtr.Zero, ShellInterop.IidShellItem, out object folderObject);

        if (hr != 0 || folderObject is not IShellItem folder)
            return results;

        IEnumShellItems? enumerator = null;

        try
        {
            folder.BindToHandler(IntPtr.Zero, ShellInterop.BhidEnumItems,
                ShellInterop.IidEnumShellItems, out object enumeratorObject);

            enumerator = enumeratorObject as IEnumShellItems;
            if (enumerator is null) return results;

            var batch = new IShellItem[1];

            while (enumerator.Next(1, batch, out uint fetched) == 0 && fetched == 1)
            {
                IShellItem item = batch[0];
                batch[0] = null!;

                try
                {
                    item.GetDisplayName(Sigdn.NormalDisplay, out string name);
                    item.GetDisplayName(Sigdn.ParentRelativeParsing, out string appUserModelId);

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appUserModelId))
                        continue;

                    BitmapSource? icon = loadIcons ? TryGetIcon(item, iconSize) : null;
                    results.Add(new InstalledApp(name, appUserModelId) { Icon = icon });
                }
                catch (COMException)
                {
                    // A single misbehaving shell extension should not abort the sweep.
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(folder);
        }

        results.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return results;
    }

    /// <summary>
    /// Icon for an arbitrary file system path (an .exe, a document, a folder).
    /// Same shell pipeline, so it also picks up per-file thumbnails.
    /// </summary>
    public static BitmapSource? GetIconForPath(string path, int iconSize = 256)
    {
        int hr = ShellInterop.SHCreateItemFromParsingName(
            path, IntPtr.Zero, ShellInterop.IidShellItem, out object itemObject);

        if (hr != 0 || itemObject is not IShellItem item)
            return null;

        try
        {
            return TryGetIcon(item, iconSize);
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    public static void Launch(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Process.Start(new ProcessStartInfo("explorer.exe", app.LaunchUri)
        {
            UseShellExecute = true
        });
    }

    private static BitmapSource? TryGetIcon(IShellItem item, int size)
    {
        if (item is not IShellItemImageFactory factory)
            return null;

        IntPtr bitmap = IntPtr.Zero;

        try
        {
            int hr = factory.GetImage(new NativeSize(size, size),
                Siigbf.IconOnly | Siigbf.BiggerSizeOk, out bitmap);

            return hr != 0 ? null : ShellInterop.ToBitmapSource(bitmap);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) ShellInterop.DeleteObject(bitmap);
        }
    }
}
