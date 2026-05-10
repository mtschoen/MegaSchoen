#if WINDOWS
using Claude.Core.Models;
using MegaSchoen.Platforms.Windows.Services;
#endif

namespace MegaSchoen
{
    public partial class DisplayManagerPage : ContentPage
    {
        public DisplayManagerPage()
        {
            InitializeComponent();
        }

        void OnCyclePermsClicked(object? sender, EventArgs eventArguments)
        {
#if WINDOWS
            CycleClaude(filter: WaitingReason.Permission);
#else
            CycleClaudeStatusLabel.Text = "Windows only";
#endif
        }

        void OnCycleAnyWaitingClicked(object? sender, EventArgs eventArguments)
        {
#if WINDOWS
            CycleClaude(filter: null);
#else
            CycleClaudeStatusLabel.Text = "Windows only";
#endif
        }

#if WINDOWS
        void CycleClaude(WaitingReason? filter)
        {
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
                cycler.CycleToNext(filter);
                var label = filter is null ? "any-waiting" : filter.ToString();
                CycleClaudeStatusLabel.Text = $"CycleToNext({label}) returned at {DateTimeOffset.UtcNow:O}";
            }
            catch (Exception exception)
            {
                CycleClaudeStatusLabel.Text = $"Threw: {exception.GetType().Name}: {exception.Message}";
                Claude.Core.Logger.Log($"CycleClaude({filter}) threw: {exception}");
            }
        }
#endif
    }
}
