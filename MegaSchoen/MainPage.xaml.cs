using System.Diagnostics;

namespace MegaSchoen
{
    public partial class MainPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void OnOneScreenButtonClicked(object sender, EventArgs e)
        {
            Debug.WriteLine("One Screen");
        }

        private void OnAllScreenButtonClicked(object? sender, EventArgs e)
        {
            Debug.WriteLine("All Screens");

            // Connect all displays
            //DisplayManager.EnableAllDisplays();
        }

        private void OnDebugDisplaysButtonClicked(object sender, EventArgs e)
        {
            Debug.WriteLine("=== Display Debug Info ===");
            
            //var displays = DisplayManager.GetAllDisplays();
            //Debug.WriteLine($"Found {displays.Count} displays:");
            
            //for (int i = 0; i < displays.Count; i++)
            //{
            //    var display = displays[i];
            //    Debug.WriteLine($"Display {i + 1}:");
            //    Debug.WriteLine($"  Device Name: {display.DeviceName}");
            //    Debug.WriteLine($"  Device String: {display.DeviceString}");
            //    Debug.WriteLine($"  Resolution: {display.Width}x{display.Height}");
            //    Debug.WriteLine($"  Position: ({display.PositionX}, {display.PositionY})");
            //    Debug.WriteLine($"  Frequency: {display.Frequency}Hz");
            //    Debug.WriteLine($"  Bits Per Pixel: {display.BitsPerPixel}");
            //    Debug.WriteLine($"  Is Active: {display.IsActive}");
            //    Debug.WriteLine($"  Is Primary: {display.IsPrimary}");
            //    Debug.WriteLine("");
            //}
            
            //Debug.WriteLine("=== End Display Debug Info ===");
        }
    }

}
