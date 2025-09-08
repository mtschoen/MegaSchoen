using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MegaSchoen
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

        public static void EnableAllDisplays()
        {
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);

            int deviceIndex = 0;
            while (EnumDisplayDevices(null, deviceIndex, ref d, 0) != 0)
            {
                Debug.WriteLine($"Found display device: {d.DeviceName}");

                //if ((d.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0)
                //{
                //    Debug.WriteLine($"Display device {d.DeviceName} is not active.");
                //    deviceIndex++;
                //    continue;
                //}

                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(dm);

                //if (EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
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
                //else
                //{
                //    Debug.WriteLine($"Failed to get current settings for {d.DeviceName}");
                //}

                deviceIndex++;
            }

            // Apply the changes
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