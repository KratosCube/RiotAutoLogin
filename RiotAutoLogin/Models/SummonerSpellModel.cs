using System.Windows.Media.Imaging;

namespace RiotAutoLogin.Models
{
public class SummonerSpellModel
{
        public string? Name { get; set; }
    public int Id { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public BitmapImage? Image { get; set; }
    }
}