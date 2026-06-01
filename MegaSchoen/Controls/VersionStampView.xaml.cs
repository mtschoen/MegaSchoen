using Claude.Core;

namespace MegaSchoen.Controls;

public partial class VersionStampView : ContentView
{
    public VersionStampView()
    {
        InitializeComponent();
        VersionLabel.Text = $"v{BuildInfo.Version}";
    }
}
