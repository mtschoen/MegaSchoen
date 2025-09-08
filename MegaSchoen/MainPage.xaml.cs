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
            DisplayManager.EnableAllDisplays();
        }
    }

}
