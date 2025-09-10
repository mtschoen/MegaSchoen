namespace DisplayManager.Core
{
    public class DisplayInfo
    {
        public string DeviceName { get; set; } = "";
        public string DeviceString { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public int Frequency { get; set; }
        public bool IsActive { get; set; }
        public bool IsPrimary { get; set; }
        public int BitsPerPixel { get; set; }
        public int StateFlags { get; set; }
        public string DeviceID { get; set; } = "";
        public string DeviceKey { get; set; } = "";
        public string SettingsSource { get; set; } = ""; // "current", "registry", or "none"
        public string MonitorName { get; set; } = "";
        public string MonitorID { get; set; } = "";
        public int MonitorStateFlags { get; set; }
    }
}