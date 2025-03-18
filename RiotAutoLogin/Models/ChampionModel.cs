using System.Windows.Media.Imaging;

public class ChampionModel
{
    public string Name { get; set; }
    public int Id { get; set; }
    public bool IsAvailable { get; set; }
    public string ImageUrl { get; set; }
    public BitmapImage Image { get; set; }
}