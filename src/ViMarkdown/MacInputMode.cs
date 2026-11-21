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

    // Deliver JIS Eisu to this application's key window, rather than posting
    // a system-wide key event (which requires Accessibility permission).
    public static void SendEisu(Window owner)
    {
        if (!OperatingSystem.IsMacOS() || !owner.IsActive || owner.TryGetPlatformHandle() is null) return;
        var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        var window = Send(app, Selector("keyWindow"));
        if (window == IntPtr.Zero) return;
        var responder = Send(window, Selector("firstResponder"));
        var inputContext = Send(responder, Selector("inputContext"));
        var number = Send(window, Selector("windowNumber"));
        var empty = String(Send(GetClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), "");
        try
        {
            var eventClass = GetClass("NSEvent");
            var constructor = Selector("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:");
            foreach (var type in new nuint[] { 10, 11 }) // keyDown, keyUp
            {
                var keyEvent = KeyEvent(eventClass, constructor, type, default, 0, 0,
                    number, IntPtr.Zero, empty, empty, false, 0x66);
                if (keyEvent == IntPtr.Zero) continue;
                // RichSurface has no managed IME client. Deliver directly to
                // Cocoa's input context so Eisu works in Normal/Visual too.
                var handled = inputContext != IntPtr.Zero && HandleEvent(inputContext, Selector("handleEvent:"), keyEvent);
                if (!handled) SendEvent(app, Selector("sendEvent:"), keyEvent);
                if (type == 10 && !handled) SelectAsciiSource();
            }
        }
        finally { Send(empty, Selector("release")); }
    }

    private static void SelectAsciiSource()
    {
        // A view without an active input context can ignore Eisu. Select the
        // remembered ASCII-capable source in that case, without global keys.
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
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public double X, Y; }
    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr String(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendEvent(IntPtr receiver, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HandleEvent(IntPtr receiver, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr KeyEvent(IntPtr receiver, IntPtr selector, nuint type,
        NativePoint location, nuint modifiers, double timestamp, nint windowNumber,
        IntPtr context, IntPtr characters, IntPtr unmodifiedCharacters,
        [MarshalAs(UnmanagedType.I1)] bool repeat, ushort keyCode);
}
