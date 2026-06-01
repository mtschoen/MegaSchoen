using DisplayManager.Core.Models;
using DisplayManager.Core.Services;
using MegaSchoen.ViewModels;

namespace MegaSchoen
{
    public partial class App : Application
    {
        // Pass this on the command line to boot straight into the Layout Editor as the sole
        // window — the screenshot pipeline (screenshot-editor.ps1) relies on it so the capture
        // harness has a single, deterministically-titled target instead of driving the UI.
        const string ScreenshotEditorArg = "--screenshot-editor";

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            if (Environment.GetCommandLineArgs().Any(
                    a => a.Equals(ScreenshotEditorArg, StringComparison.OrdinalIgnoreCase)))
            {
                return CreateScreenshotEditorWindow();
            }

            return new Window(new AppShell());
        }

        static Window CreateScreenshotEditorWindow()
        {
            var profile = BuildScreenshotProfile();
            var viewModel = new LayoutEditorViewModel(profile);
            // The editor now selects a monitor on open itself, so the capture shows the full
            // (selection-expanded) panel without any harness-side selection.
            return new Window(new LayoutEditorPage(viewModel))
            {
                Title = $"Edit Layout — {profile.Name}",
                Width = 1100,
                Height = 880
            };
        }

        // Prefer the live desktop layout (it's what the user actually sees), but fall back to a
        // synthetic two-monitor layout so the editor still renders with no displays available
        // (headless / RDP), keeping the screenshot pipeline usable everywhere.
        static SavedDisplayProfile BuildScreenshotProfile()
        {
            try
            {
                var profile = new DisplayProfileService().CaptureCurrentConfiguration("Screenshot");
                if (profile.Displays.Count > 0)
                {
                    return profile;
                }
            }
            catch
            {
                /* Native enumeration unavailable (headless / RDP): fall through to
                   the synthetic layout so the screenshot pipeline still works. */
            }

            return new SavedDisplayProfile
            {
                Name = "Screenshot",
                Displays =
                [
                    new SavedDisplayConfig
                    {
                        MonitorName = "Primary 2560×1440",
                        Width = 2560, Height = 1440, PositionX = 0, PositionY = 0,
                        RefreshRate = 144, IsPrimary = true
                    },
                    new SavedDisplayConfig
                    {
                        MonitorName = "Secondary 1920×1080",
                        Width = 1920, Height = 1080, PositionX = 2560, PositionY = 180,
                        RefreshRate = 60
                    }
                ]
            };
        }
    }
}
