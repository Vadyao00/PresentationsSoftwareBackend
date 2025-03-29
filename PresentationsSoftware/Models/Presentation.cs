namespace PresentationsSoftware.Models;

public class Presentation
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Author { get; set; }
    public DateTime UploadDate { get; set; }
    public List<Slide> Slides { get; set; } = new List<Slide>();
    public List<PresentationUser> Users { get; set; } = new List<PresentationUser>();
}