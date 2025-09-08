using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DisplayManager.Core
{
    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        private static extern int EnumDisplayDevices(string lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISPLAY_DEVICE_ACTIVE = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        public static List<DisplayInfo> GetAllDisplays()
        {
            var displays = new List<DisplayInfo>();
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);

            int deviceIndex = 0;
            while (EnumDisplayDevices(null, deviceIndex, ref d, 0) != 0)
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(dm);

                if (EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    var display = new DisplayInfo
                    {
                        DeviceName = d.DeviceName,
                        DeviceString = d.DeviceString,
                        Width = dm.dmPelsWidth,
                        Height = dm.dmPelsHeight,
                        PositionX = dm.dmPositionX,
                        PositionY = dm.dmPositionY,
                        Frequency = dm.dmDisplayFrequency,
                        BitsPerPixel = dm.dmBitsPerPel,
                        IsActive = (d.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0,
                        IsPrimary = dm.dmPositionX == 0 && dm.dmPositionY == 0
                    };
                    displays.Add(display);
                }

                deviceIndex++;
            }

            return displays;
        }

        public static bool SetDisplayConfiguration(List<DisplayInfo> displays)
        {
            bool allSuccessful = true;

            foreach (var display in displays)
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(dm);
                dm.dmPelsWidth = display.Width;
                dm.dmPelsHeight = display.Height;
                dm.dmPositionX = display.PositionX;
                dm.dmPositionY = display.PositionY;
                dm.dmDisplayFrequency = display.Frequency;
                dm.dmBitsPerPel = display.BitsPerPixel;
                dm.dmFields = 0x20000 | 0x80000 | 0x100000 | 0x400000 | 0x40000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL | DM_POSITION

                uint flags = display.IsActive ? CDS_UPDATEREGISTRY : (uint)0;
                int result = ChangeDisplaySettingsEx(display.DeviceName, ref dm, IntPtr.Zero, flags, IntPtr.Zero);

                if (result != DISP_CHANGE_SUCCESSFUL)
                {
                    Debug.WriteLine($"Failed to set display configuration for {display.DeviceName}, error code: {result}");
                    allSuccessful = false;
                }
            }

            return allSuccessful;
        }

        public static bool DisableAllDisplaysExceptPrimary()
        {
            var displays = GetAllDisplays();
            bool allSuccessful = true;

            foreach (var display in displays)
            {
                if (!display.IsPrimary && display.IsActive)
                {
                    DEVMODE dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(dm);
                    dm.dmFields = 0x40000; // DM_POSITION
                    dm.dmPelsWidth = 0;
                    dm.dmPelsHeight = 0;

                    int result = ChangeDisplaySettingsEx(display.DeviceName, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
                    if (result != DISP_CHANGE_SUCCESSFUL)
                    {
                        Debug.WriteLine($"Failed to disable display {display.DeviceName}, error code: {result}");
                        allSuccessful = false;
                    }
                }
            }

            return allSuccessful;
        }

        public static void EnableAllDisplays()
        {
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);

            int deviceIndex = 0;
            while (EnumDisplayDevices(null, deviceIndex, ref d, 0) != 0)
            {
                Debug.WriteLine($"Found display device: {d.DeviceName}");

                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(dm);

                if (EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    Debug.WriteLine($"Current settings for {d.DeviceName}: {dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz");

                    dm.dmDisplayFlags = DISPLAY_DEVICE_ACTIVE;
                    int result = ChangeDisplaySettingsEx(d.DeviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                    if (result != DISP_CHANGE_SUCCESSFUL)
                    {
                        Debug.WriteLine($"Failed to enable display for {d.DeviceName}, error code: {result}");
                    }
                    else
                    {
                        Debug.WriteLine($"Successfully enabled display for {d.DeviceName}");
                    }
                }

                deviceIndex++;
            }

            DEVMODE devMode = default;
            int finalResult = ChangeDisplaySettingsEx(null, ref devMode, IntPtr.Zero, 0, IntPtr.Zero);
            if (finalResult != DISP_CHANGE_SUCCESSFUL)
            {
                Debug.WriteLine($"Failed to apply display changes, error code: {finalResult}");
            }
            else
            {
                Debug.WriteLine("Successfully applied display changes");
            }
        }
    }
}