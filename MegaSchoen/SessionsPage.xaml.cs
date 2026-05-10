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
#else
    public SessionsPage()
    {
        InitializeComponent();
    }
#endif
}
