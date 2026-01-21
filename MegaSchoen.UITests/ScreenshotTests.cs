using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MegaSchoen.UITests;

[TestClass]
public sealed class ScreenshotTests
{
    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    static readonly string AppPath = Path.GetFullPath("../../../../MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/MegaSchoen.exe");
    static readonly string ScreenshotPath = Path.GetFullPath("../../../../Screenshots");

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

    const int PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hWnd);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    static IntPtr FindWindowByProcessId(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            if (windowProcessId == processId && IsWindowVisible(hWnd))
            {
                var text = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, text, text.Capacity);

                // Make sure it's a main window, not a child window
                if (text.Length > 0)
                {
                    Console.WriteLine($"Found window: '{text}' (PID: {windowProcessId})");
                    result = hWnd;
                    return false; // Stop enumeration
                }
            }
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return result;
    }

    [TestMethod]
    public void CaptureMainWindowScreenshot()
    {
        Console.WriteLine($"Launching app: {AppPath}");

        Process? appProcess = null;
        try
        {
            // Launch the app
            appProcess = Process.Start(new ProcessStartInfo
            {
                FileName = AppPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });

            Assert.IsNotNull(appProcess, "Failed to start application");

            // Wait for the app to fully initialize and render
            Console.WriteLine("Waiting for app to initialize...");

            // Wait and periodically check if process is still alive
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(500);
                appProcess.Refresh();

                if (appProcess.HasExited)
                {
                    Assert.Fail(
                        $"Application exited with code {appProcess.ExitCode} before creating a window.\n\n" +
                        "This usually means Windows App SDK runtime is not installed.\n" +
                        "Please install it from: https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe\n\n" +
                        "Or install via winget:\n" +
                        "  winget install Microsoft.WindowsAppRuntime.1.6");
                }
            }

            // Find the window by process ID
            Console.WriteLine($"Looking for window with process ID: {appProcess.Id}");
            IntPtr windowHandle = FindWindowByProcessId(appProcess.Id);

            if (windowHandle == IntPtr.Zero)
            {
                Assert.Fail($"Could not find window for process {appProcess.Id}. The app may not have created a window yet.");
            }

            // Get window dimensions
            if (!GetWindowRect(windowHandle, out RECT rect))
            {
                Assert.Fail("Failed to get window dimensions");
            }

            int windowWidth = rect.Right - rect.Left;
            int windowHeight = rect.Bottom - rect.Top;

            // Get DPI scaling factor
            uint dpi = GetDpiForWindow(windowHandle);
            double scaleFactor = dpi / 96.0; // 96 is the standard DPI

            Console.WriteLine($"Window rect size: {windowWidth}x{windowHeight}");
            Console.WriteLine($"DPI: {dpi} (scale factor: {scaleFactor:F2})");

            // Create bitmap large enough to capture scaled content
            int bitmapWidth = (int)(windowWidth * scaleFactor);
            int bitmapHeight = (int)(windowHeight * scaleFactor);

            Console.WriteLine($"Bitmap size: {bitmapWidth}x{bitmapHeight}");

            // Capture the window
            using var bitmap = new Bitmap(bitmapWidth, bitmapHeight, PixelFormat.Format32bppArgb);
            bitmap.SetResolution(dpi, dpi);

            using var graphics = Graphics.FromImage(bitmap);
            graphics.PageUnit = GraphicsUnit.Pixel;

            var hdc = graphics.GetHdc();

            try
            {
                bool success = PrintWindow(windowHandle, hdc, PW_RENDERFULLCONTENT);
                Console.WriteLine($"PrintWindow result: {success}");
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            if (!Directory.Exists(ScreenshotPath))
                Directory.CreateDirectory(ScreenshotPath);

            // Save screenshot
            var screenshotFile = Path.Combine(ScreenshotPath, "MegaSchoen-UI.png");
            bitmap.Save(screenshotFile, ImageFormat.Png);

            Console.WriteLine($"Screenshot saved to: {screenshotFile}");
            Assert.IsTrue(File.Exists(screenshotFile), "Screenshot file should exist");

            // Also save info about the screenshot
            var info = new FileInfo(screenshotFile);
            Console.WriteLine($"Screenshot size: {info.Length / 1024} KB");
        }
        finally
        {
            // Clean up - close the app
            if (appProcess != null && !appProcess.HasExited)
            {
                Console.WriteLine("Closing application...");
                try
                {
                    appProcess.Kill();
                    appProcess.WaitForExit(5000);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
            appProcess?.Dispose();
        }
    }
}
