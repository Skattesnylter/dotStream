using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotStream.App;

/// <summary>What Discord gave us, and when it stops being useful.</summary>
public sealed record DiscordToken(string AccessToken, string RefreshToken, DateTime ExpiresUtc)
{
    /// <summary>
    /// Treated as expired an hour early, so a key press never lands in the gap between
    /// "still valid" and "refused".
    /// </summary>
    public bool IsUsable => DateTime.UtcNow < ExpiresUtc.AddHours(-1);
}

/// <summary>
/// Keeps the Discord token between runs.
///
/// The first thing in this project that is genuinely a credential. Everything else in
/// %APPDATA% is a layout or a preference and is stored as readable JSON on purpose;
/// this one would let anyone who copied the file speak to Discord as the user, so it
/// goes through DPAPI and is tied to the Windows account.
///
/// That is not real protection against someone already running code as this user -
/// nothing on a desktop is - but it does mean the file is useless on another machine
/// and does not sit in a backup in plain text.
/// </summary>
public static class DiscordTokens
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("dotStream.discord.v1");

    public static string FilePath => Path.Combine(AppSelectionStore.DirectoryPath, "discord.dat");

    public static DiscordToken? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;

            byte[] plain = ProtectedData.Unprotect(
                File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<DiscordToken>(plain);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException)
        {
            // A token that cannot be read is a token we do not have. Asking again is
            // the correct recovery, and it costs the user one dialog.
            return null;
        }
    }

    public static void Save(DiscordToken token)
    {
        try
        {
            Directory.CreateDirectory(AppSelectionStore.DirectoryPath);

            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(token);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            DeckLog.Note("discord", "could not save the token: " + ex.Message);
        }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (IOException) { }
    }
}
