using System.Runtime.InteropServices;

namespace DotStream.App;

/// <summary>
/// The <c>SendInput</c> plumbing, in one place.
///
/// Both the hotkey action and the text macro synthesise keystrokes, and the structure
/// layout has to be exactly right or the call is rejected for its size. Copying forty
/// lines of interop into a second file to avoid sharing it would be the wrong kind of
/// tidy - there is one Windows API here, so there is one wrapper.
/// </summary>
internal static class Win32Input
{
    public const uint KeyEventKeyUp = 0x0002;

    /// <summary>The virtual key is ignored and the scan code carries a character instead.</summary>
    public const uint KeyEventUnicode = 0x0004;

    private const uint InputKeyboard = 1;

    public static Input Key(ushort virtualKey, bool down) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = down ? 0 : KeyEventKeyUp
            }
        }
    };

    /// <summary>
    /// A character rather than a key. Typing this way sidesteps the keyboard layout
    /// entirely: an "@" is an "@" whether the machine is Norwegian or American, where
    /// pressing the key that bears one is not.
    /// </summary>
    public static Input Character(char value, bool down) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                ScanCode = value,
                Flags = KeyEventUnicode | (down ? 0 : KeyEventKeyUp)
            }
        }
    };

    public static void Send(IReadOnlyList<Input> inputs)
    {
        if (inputs.Count == 0) return;

        Input[] array = inputs as Input[] ?? [.. inputs];
        SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // The union must be laid out for the largest member or SendInput rejects the size.
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
