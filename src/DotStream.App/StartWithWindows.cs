using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DotStream.App;

/// <summary>
/// Starting dotStream when you sign in.
///
/// This is the answer to "can it start when the deck is plugged in", which Windows
/// does not offer for a HID device: there is no arrival event to hang a task on
/// unless you switch on a log that records every USB event on the machine, which is
/// too invasive to ship. Running at login covers the same ground from the other side -
/// dotStream is already there, and the transport watches for a deck continuously, so
/// plugging in at any point after that just works.
///
/// HKCU\...\Run rather than a scheduled task or a service: it needs no elevation,
/// belongs to this user, and is somewhere people know to look. Anyone can see it in
/// Task Manager's Startup tab and turn it off without going through this application,
/// which is the right relationship to have with something that starts itself.
/// </summary>
public static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "dotStream";

    /// <summary>The launcher, quoted so a path with spaces survives.</summary>
    private static string Command
    {
        get
        {
            string exe = Environment.ProcessPath
                         ?? Process.GetCurrentProcess().MainModule?.FileName
                         ?? "";

            // Started at login it should appear the way it does when you leave it
            // running: in the tray, not in your face.
            return $"\"{exe}\" --tray";
        }
    }

    /// <summary>
    /// Whether dotStream is registered to start, and registered as *this* copy.
    ///
    /// The path matters. Moving or reinstalling leaves a Run entry pointing at an
    /// executable that is no longer there, and a checkbox that reports "on" while
    /// nothing starts is worse than one that reports "off".
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
                if (key?.GetValue(ValueName) is not string existing) return false;

                return PathOf(existing) is { } registered
                    && PathOf(Command) is { } current
                    && string.Equals(registered, current, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Turns it on or off. Returns false if the registry refused, which is worth
    /// telling the user about rather than leaving a checkbox that silently does not
    /// stick.
    /// </summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            DeckLog.Note("startup", $"could not {(enabled ? "enable" : "disable")} start with Windows: {ex.Message}");
            return false;
        }
    }

    /// <summary>Pulls the executable out of a command line that may be quoted.</summary>
    private static string? PathOf(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        int space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}
