using System.Runtime.InteropServices;

namespace DotStream.Media;

/// <summary>
/// System volume via the standard media keys.
///
/// Core Audio (IAudioEndpointVolume) would give finer control, but the media keys
/// respect the user's own step size and the on-screen volume overlay appears, which
/// is what makes a physical volume key feel right.
/// </summary>
public static class SystemVolume
{
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;

    private const uint KeyEventKeyUp = 0x0002;

    public static void Up() => Tap(VkVolumeUp);

    public static void Down() => Tap(VkVolumeDown);

    public static void ToggleMute() => Tap(VkVolumeMute);

    private static void Tap(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
