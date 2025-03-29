namespace PresentationsSoftware.Models;

public class Slide
{
    public int Id { get; set; }
    public int PresentationId { get; set; }
    public int Order { get; set; }
    public List<SlideElement> Elements { get; set; } = new List<SlideElement>();
    public Presentation Presentation { get; set; }
}