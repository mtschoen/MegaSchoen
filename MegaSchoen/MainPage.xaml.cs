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
            Debug.WriteLine("One Screen - Switch to internal display only");
            try 
            {
                var success = DisplayManager.Core.DisplayManager.SwitchToInternalDisplay();
                Debug.WriteLine(success ? "Successfully switched to internal display" : "Failed to switch to internal display");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error switching to internal display: {ex.Message}");
            }
        }

        private void OnAllScreenButtonClicked(object? sender, EventArgs e)
        {
            Debug.WriteLine("All Screens - Enable all displays");
            try 
            {
                var success = DisplayManager.Core.DisplayManager.EnableAllDisplays();
                Debug.WriteLine(success ? "Successfully enabled all displays" : "Failed to enable all displays");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enabling displays: {ex.Message}");
            }
        }
    }

}
