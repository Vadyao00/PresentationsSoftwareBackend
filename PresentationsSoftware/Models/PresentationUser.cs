namespace PresentationsSoftware.Models;

public class PresentationUser
{
    public int Id { get; set; }
    public string ConnectionId { get; set; }
    public string Nickname { get; set; }
    public int PresentationId { get; set; }
    public UserRole Role { get; set; }
}