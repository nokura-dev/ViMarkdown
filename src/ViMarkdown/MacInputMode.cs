using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ViMarkdown;

internal static class MacInputMode
{
    public static string? CurrentKeyText(Window owner)
    {
        // A preview has no IME client, so TextInput is not guaranteed to arrive.
        // Read the actual layout-resolved text while handling Cocoa's keyDown.
        if (!OperatingSystem.IsMacOS() || !owner.IsActive ||
            owner.TryGetPlatformHandle()?.HandleDescriptor is not ("NSWindow" or "NSView")) return null;
        var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        var keyEvent = Send(app, Selector("currentEvent"));
        if (keyEvent == IntPtr.Zero || Send(keyEvent, Selector("type")) != 10) return null;
        if (Send(keyEvent, Selector("window")) != Send(app, Selector("keyWindow"))) return null;
        var characters = Send(keyEvent, Selector("charactersIgnoringModifiers"));
        if (characters == IntPtr.Zero) return null;
        var text = Marshal.PtrToStringUTF8(Send(characters, Selector("UTF8String")));
        return string.IsNullOrEmpty(text) || text.Any(c => char.IsControl(c) ||
            char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.PrivateUse) ? null : text;
    }

    // Select the remembered ASCII input source directly. Synthesizing Eisu
    // events makes Cocoa's Japanese IME emit kana/eisu diagnostic messages.
    public static void SendEisu(Window owner)
    {
        if (!OperatingSystem.IsMacOS() || !owner.IsActive || owner.TryGetPlatformHandle() is null) return;
        SelectAsciiSource();
    }

    private static void SelectAsciiSource()
    {
        // Change this application's input source without synthesizing key events.
        var source = TISCopyCurrentASCIICapableKeyboardInputSource();
        if (source == IntPtr.Zero) return;
        try { TISSelectInputSource(source); }
        finally { CFRelease(source); }
    }

    private const string Hitoolbox = "/System/Library/Frameworks/Carbon.framework/Frameworks/HIToolbox.framework/HIToolbox";
    [DllImport(Hitoolbox)]
    private static extern IntPtr TISCopyCurrentASCIICapableKeyboardInputSource();
    [DllImport(Hitoolbox)]
    private static extern int TISSelectInputSource(IntPtr source);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
}
