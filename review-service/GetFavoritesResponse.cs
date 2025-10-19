using System.Collections.Generic;

public class GetFavoritesResponse
{
    public IEnumerable<FavoriteSpotItem> Items { get; set; } = new List<FavoriteSpotItem>();
}
