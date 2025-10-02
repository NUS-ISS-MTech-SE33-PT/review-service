public class Review
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SpotId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public int Rating { get; set; }
    public string Text { get; set; } = default!;
    public string[]? PhotoUrls { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}