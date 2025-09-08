namespace MegaSchoen
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
    }
}