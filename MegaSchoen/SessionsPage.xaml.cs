namespace MegaSchoen;

public partial class SessionsPage : ContentPage
{
#if WINDOWS
    readonly MegaSchoen.ViewModels.SessionsPageViewModel _viewModel;

    public SessionsPage(MegaSchoen.ViewModels.SessionsPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Dispose();
    }

    void OnCyclePermsClicked(object? sender, EventArgs eventArguments) =>
        CycleClaude(filter: Claude.Core.Models.WaitingReason.Permission);

    void OnCycleAnyWaitingClicked(object? sender, EventArgs eventArguments) =>
        CycleClaude(filter: null);

    void CycleClaude(Claude.Core.Models.WaitingReason? filter)
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
            var cycler = services.GetService(typeof(MegaSchoen.Platforms.Windows.Services.ClaudeWindowService))
                as MegaSchoen.Platforms.Windows.Services.ClaudeWindowService;
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
#else
    public SessionsPage()
    {
        InitializeComponent();
    }

    void OnCyclePermsClicked(object? sender, EventArgs eventArguments) =>
        CycleClaudeStatusLabel.Text = "Windows only";

    void OnCycleAnyWaitingClicked(object? sender, EventArgs eventArguments) =>
        CycleClaudeStatusLabel.Text = "Windows only";
#endif
}
