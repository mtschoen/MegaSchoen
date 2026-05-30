namespace DisplayManager.Core.Models;

/// <summary>A supported display mode (resolution + refresh) for a monitor.</summary>
public class DisplayMode
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double RefreshRate { get; set; }
}
