using System;

public class FavoriteSpotItem
{
    public string SpotId { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public SpotDocument? Spot { get; set; }
}
