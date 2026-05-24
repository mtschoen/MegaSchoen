namespace Claude.Core.Models;

// Opaque wrapper around a Win32 HWND so SessionSnapshot doesn't leak IntPtr
// into consumer code. Internal fields are exposed only to platform impls
// inside Claude.Core.
public readonly record struct WindowToken
{
    internal IntPtr Handle { get; init; }

    public static WindowToken FromHandle(IntPtr handle) => new() { Handle = handle };
    public static WindowToken Null { get; } = new();

    public bool IsZero => Handle == IntPtr.Zero;
}
