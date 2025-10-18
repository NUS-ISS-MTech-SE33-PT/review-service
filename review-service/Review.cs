public class Review
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SpotId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public double Rating { get; set; }
    public double TasteRating { get; set; }
    public double EnvironmentRating { get; set; }
    public double ServiceRating { get; set; }
    public string Text { get; set; } = default!;
    public string[]? PhotoUrls { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
