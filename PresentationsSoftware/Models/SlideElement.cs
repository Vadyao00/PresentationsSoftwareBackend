namespace PresentationsSoftware.Models;

public class SlideElement
{
    public int Id { get; set; }
    public int SlideId { get; set; }
    public ElementType Type { get; set; }
    public string Content { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Color { get; set; }
    public string Style { get; set; }
    public Slide Slide { get; set; }
}