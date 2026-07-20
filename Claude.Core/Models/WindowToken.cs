namespace Claude.Core.Models;

// Opaque focus handle so SessionSnapshot doesn't leak IntPtr into consumer
// code: a Win32 HWND on Windows, the claude PID on Linux (resolved to the
// hosting terminal window by LinuxClaudeWindowFocuser at focus time).
// Internal fields are exposed only to platform impls inside Claude.Core.
public readonly record struct WindowToken
{
    internal IntPtr Handle { get; init; }

    public static WindowToken FromHandle(IntPtr handle) => new() { Handle = handle };
    public static WindowToken Null => default;

    public bool IsZero => Handle == IntPtr.Zero;
}
