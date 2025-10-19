using System.Collections.Generic;

public class CenterSummaryDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
}

public class SpotDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string FoodType { get; set; } = string.Empty;
    public double Rating { get; set; }
    public string OpeningHours { get; set; } = string.Empty;
    public List<string> Photos { get; set; } = new();
    public string PlaceType { get; set; } = "Hawker Stall";
    public bool Open { get; set; }
    public bool IsCenter { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AvgPrice { get; set; }
    public double? TasteAvg { get; set; }
    public double? ServiceAvg { get; set; }
    public double? EnvironmentAvg { get; set; }
    public string? District { get; set; }
    public string? ThumbnailUrl { get; set; }
    public CenterSummaryDocument? ParentCenter { get; set; }
}
