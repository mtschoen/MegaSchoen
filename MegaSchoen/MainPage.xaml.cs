#if WINDOWS
using MegaSchoen.Platforms.Windows.Services;
#endif

namespace MegaSchoen
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        void OnCycleClaudeNowClicked(object? sender, EventArgs eventArguments)
        {
#if WINDOWS
            try
            {
                var services = Microsoft.UI.Xaml.Application.Current is MegaSchoen.WinUI.App
                    ? MauiWinUIApplication.Current.Services
                    : null;
                if (services is null)
                {
                    CycleClaudeStatusLabel.Text = "DI container not available";
                    return;
                }
                var cycler = services.GetService(typeof(ClaudeWindowService)) as ClaudeWindowService;
                if (cycler is null)
                {
                    CycleClaudeStatusLabel.Text = "ClaudeWindowService not resolved";
                    return;
                }
                cycler.CycleToNext();
                CycleClaudeStatusLabel.Text = $"CycleToNext returned at {DateTimeOffset.UtcNow:O}";
            }
            catch (Exception exception)
            {
                CycleClaudeStatusLabel.Text = $"Threw: {exception.GetType().Name}: {exception.Message}";
                ClaudeCycler.Core.Logger.Log($"OnCycleClaudeNowClicked threw: {exception}");
            }
#else
            CycleClaudeStatusLabel.Text = "Windows only";
#endif
        }
    }
}
