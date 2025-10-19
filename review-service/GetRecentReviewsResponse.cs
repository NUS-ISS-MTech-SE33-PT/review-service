using System.Collections.Generic;

public class GetRecentReviewsResponse
{
    public IEnumerable<RecentReviewItem> Items { get; set; } = new List<RecentReviewItem>();
}
