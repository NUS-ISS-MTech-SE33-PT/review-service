public class CreateReviewRequest
{
    public int Rating { get; set; }
    public string Text { get; set; } = default!;
    public string[]? PhotoUrls { get; set; }
}