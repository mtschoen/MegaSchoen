namespace MegaSchoen
{
    public class DisplayConfiguration
    {
        public string Name { get; set; } = "";
        public List<DisplayInfo> Displays { get; set; } = new();
        public DateTime Created { get; set; } = DateTime.Now;
    }
}