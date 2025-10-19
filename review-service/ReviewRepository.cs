using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

public class ReviewRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _reviewTableName;
    private readonly string _spotTableName;
    private readonly string _favoriteTableName;
    private readonly ILogger<ReviewRepository> _logger;

    public ReviewRepository(
        IAmazonDynamoDB dynamoDb,
        IConfiguration configuration,
        ILogger<ReviewRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _reviewTableName = configuration["DynamoDb"]
            ?? throw new InvalidOperationException("Review table name is not configured.");
        _spotTableName = configuration["SpotDynamoDb"]
            ?? throw new InvalidOperationException("Spot table name is not configured.");
        _favoriteTableName = configuration["FavoriteDynamoDb"]
            ?? throw new InvalidOperationException("Favorite table name is not configured.");
        _logger = logger;
    }

    public async Task SaveAsync(Review review, CancellationToken cancellationToken = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["spotId"] = new AttributeValue { S = review.SpotId },
            ["id"] = new AttributeValue { S = review.Id },
            ["userId"] = new AttributeValue { S = review.UserId },
            ["rating"] = new AttributeValue { N = FormatAverage(review.Rating) },
            ["tasteRating"] = new AttributeValue { N = FormatAverage(review.TasteRating) },
            ["environmentRating"] = new AttributeValue { N = FormatAverage(review.EnvironmentRating) },
            ["serviceRating"] = new AttributeValue { N = FormatAverage(review.ServiceRating) },
            ["text"] = new AttributeValue { S = review.Text },
            ["createdAt"] = new AttributeValue
            {
                N = ((DateTimeOffset)review.CreatedAt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            }
        };

        if (review.PhotoUrls != null && review.PhotoUrls.Length > 0)
        {
            item["photoUrls"] = new AttributeValue { SS = review.PhotoUrls.ToList() };
        }

        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _reviewTableName,
            Item = item
        }, cancellationToken);

        await UpdateSpotAggregatesAsync(review, cancellationToken);
    }

    public async Task<List<Review>> GetBySpotIdOrderByCreatedAtDescendingAsync(string spotId, CancellationToken cancellationToken = default)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _reviewTableName,
            IndexName = "reviews_by_createdAt",
            KeyConditionExpression = "spotId = :spotId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":spotId"] = new AttributeValue { S = spotId }
            },
            ScanIndexForward = false
        }, cancellationToken);

        return response.Items.Select(ToReview).ToList();
    }

    public async Task<List<Review>> GetByUserIdOrderByCreatedAtDescendingAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _reviewTableName,
            IndexName = "reviews_by_user",
            KeyConditionExpression = "userId = :userId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId }
            },
            ScanIndexForward = false
        }, cancellationToken);

        return response.Items.Select(ToReview).ToList();
    }

    public async Task<IReadOnlyList<RecentReviewItem>> GetRecentReviewsWithSpotAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Array.Empty<RecentReviewItem>();
        }

        var reviews = await ScanRecentReviewsAsync(limit, cancellationToken);
        if (reviews.Count == 0)
        {
            return Array.Empty<RecentReviewItem>();
        }

        var spotMap = await FetchSpotsAsync(reviews.Select(r => r.SpotId), cancellationToken);
        return reviews
            .Select(review => new RecentReviewItem
            {
                Review = review,
                Spot = spotMap.TryGetValue(review.SpotId, out var item)
                    ? ToSpotDocument(item)
                    : null
            })
            .ToList();
    }

    public async Task<IReadOnlyList<FavoriteSpotItem>> GetFavoritesWithSpotAsync(string userId, CancellationToken cancellationToken = default)
    {
        var favorites = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _favoriteTableName,
            KeyConditionExpression = "userId = :userId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId }
            },
            ScanIndexForward = false
        }, cancellationToken);

        var records = favorites.Items
            .Select(item =>
            {
                if (!item.TryGetValue("spotId", out var spotAttr) || string.IsNullOrEmpty(spotAttr.S))
                {
                    return null;
                }

                var createdAt = item.TryGetValue("createdAt", out var createdAttr) && !string.IsNullOrEmpty(createdAttr.N)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(createdAttr.N, CultureInfo.InvariantCulture)).UtcDateTime
                    : DateTime.UtcNow;

                return new FavoriteSpotItem
                {
                    SpotId = spotAttr.S,
                    AddedAt = createdAt
                };
            })
            .Where(record => record != null)
            .Cast<FavoriteSpotItem>()
            .ToList();

        if (records.Count == 0)
        {
            return Array.Empty<FavoriteSpotItem>();
        }

        var spotMap = await FetchSpotsAsync(records.Select(f => f.SpotId), cancellationToken);
        foreach (var record in records)
        {
            record.Spot = spotMap.TryGetValue(record.SpotId, out var item)
                ? ToSpotDocument(item)
                : null;
        }

        return records;
    }

    public async Task<bool> IsFavoriteAsync(string userId, string spotId, CancellationToken cancellationToken = default)
    {
        var response = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _favoriteTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["userId"] = new AttributeValue { S = userId },
                ["spotId"] = new AttributeValue { S = spotId }
            }
        }, cancellationToken);

        return response.Item != null && response.Item.Count > 0;
    }

    public Task AddFavoriteAsync(string userId, string spotId, CancellationToken cancellationToken = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"] = new AttributeValue { S = userId },
            ["spotId"] = new AttributeValue { S = spotId },
            ["createdAt"] = new AttributeValue
            {
                N = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            }
        };

        return _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _favoriteTableName,
            Item = item
        }, cancellationToken);
    }

    public Task RemoveFavoriteAsync(string userId, string spotId, CancellationToken cancellationToken = default)
    {
        return _dynamoDb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _favoriteTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["userId"] = new AttributeValue { S = userId },
                ["spotId"] = new AttributeValue { S = spotId }
            }
        }, cancellationToken);
    }

    private Review ToReview(Dictionary<string, AttributeValue> item)
    {
        var rating = ParseDouble(item["rating"].N);
        return new Review
        {
            Id = item["id"].S,
            SpotId = item["spotId"].S,
            UserId = item["userId"].S,
            Rating = rating,
            TasteRating = TryParseDouble(item, "tasteRating", rating),
            EnvironmentRating = TryParseDouble(item, "environmentRating", rating),
            ServiceRating = TryParseDouble(item, "serviceRating", rating),
            Text = item["text"].S,
            PhotoUrls = item.TryGetValue("photoUrls", out var value) ? value.SS.ToArray() : null,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(item["createdAt"].N, CultureInfo.InvariantCulture)).UtcDateTime
        };
    }

    private async Task<List<Review>> ScanRecentReviewsAsync(int limit, CancellationToken cancellationToken)
    {
        var collected = new List<Review>();
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;

        do
        {
            var response = await _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _reviewTableName,
                ExclusiveStartKey = lastEvaluatedKey
            }, cancellationToken);

            collected.AddRange(response.Items.Select(ToReview));
            lastEvaluatedKey = response.LastEvaluatedKey;
        } while (lastEvaluatedKey != null && lastEvaluatedKey.Count > 0);

        return collected
            .OrderByDescending(review => review.CreatedAt)
            .Take(limit)
            .ToList();
    }

    private async Task<Dictionary<string, Dictionary<string, AttributeValue>>> FetchSpotsAsync(IEnumerable<string> spotIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Dictionary<string, AttributeValue>>(StringComparer.Ordinal);
        var uniqueIds = spotIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (uniqueIds.Count == 0)
        {
            return result;
        }

        const int batchSize = 100;
        for (var i = 0; i < uniqueIds.Count; i += batchSize)
        {
            var batch = uniqueIds.Skip(i).Take(batchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var request = new BatchGetItemRequest
            {
                RequestItems = new Dictionary<string, KeysAndAttributes>
                {
                    [_spotTableName] = new KeysAndAttributes
                    {
                        Keys = batch.Select(id => new Dictionary<string, AttributeValue>
                        {
                            ["id"] = new AttributeValue { S = id }
                        }).ToList()
                    }
                }
            };

            var response = await _dynamoDb.BatchGetItemAsync(request, cancellationToken);
            if (response.Responses.TryGetValue(_spotTableName, out var items))
            {
                foreach (var item in items)
                {
                    if (item.TryGetValue("id", out var idAttr) && !string.IsNullOrEmpty(idAttr.S))
                    {
                        result[idAttr.S] = item;
                    }
                }
            }

            var unprocessed = response.UnprocessedKeys;
            while (unprocessed != null && unprocessed.Count > 0)
            {
                var retry = await _dynamoDb.BatchGetItemAsync(new BatchGetItemRequest
                {
                    RequestItems = unprocessed
                }, cancellationToken);

                if (retry.Responses.TryGetValue(_spotTableName, out var retryItems))
                {
                    foreach (var item in retryItems)
                    {
                        if (item.TryGetValue("id", out var idAttr) && !string.IsNullOrEmpty(idAttr.S))
                        {
                            result[idAttr.S] = item;
                        }
                    }
                }

                unprocessed = retry.UnprocessedKeys;
            }
        }

        return result;
    }

    private SpotDocument? ToSpotDocument(Dictionary<string, AttributeValue> item)
    {
        if (!item.TryGetValue("id", out var idAttr) || string.IsNullOrEmpty(idAttr.S))
        {
            return null;
        }

        var document = new SpotDocument
        {
            Id = idAttr.S,
            Name = item.TryGetValue("name", out var nameAttr) ? nameAttr.S ?? string.Empty : string.Empty,
            Address = item.TryGetValue("address", out var addressAttr) ? addressAttr.S ?? string.Empty : string.Empty,
            FoodType = item.TryGetValue("foodType", out var foodTypeAttr) ? foodTypeAttr.S ?? string.Empty : string.Empty,
            Rating = item.TryGetValue("rating", out var ratingAttr) && !string.IsNullOrEmpty(ratingAttr.N) ? ParseDouble(ratingAttr.N) : 0d,
            OpeningHours = item.TryGetValue("openingHours", out var hoursAttr) ? hoursAttr.S ?? string.Empty : string.Empty,
            PlaceType = item.TryGetValue("placeType", out var placeTypeAttr) ? placeTypeAttr.S ?? "Hawker Stall" : "Hawker Stall",
            Open = item.TryGetValue("open", out var openAttr) && openAttr.BOOL.HasValue && openAttr.BOOL.Value,
            IsCenter = item.TryGetValue("isCenter", out var centerAttr) && centerAttr.BOOL.HasValue && centerAttr.BOOL.Value,
            Latitude = item.TryGetValue("latitude", out var latAttr) && !string.IsNullOrEmpty(latAttr.N) ? ParseDouble(latAttr.N) : 0d,
            Longitude = item.TryGetValue("longitude", out var lonAttr) && !string.IsNullOrEmpty(lonAttr.N) ? ParseDouble(lonAttr.N) : 0d,
            AvgPrice = item.TryGetValue("avgPrice", out var avgAttr) && !string.IsNullOrEmpty(avgAttr.N) ? ParseDouble(avgAttr.N) : (double?)null,
            TasteAvg = item.TryGetValue("tasteAvg", out var tasteAttr) && !string.IsNullOrEmpty(tasteAttr.N) ? ParseDouble(tasteAttr.N) : (double?)null,
            ServiceAvg = item.TryGetValue("serviceAvg", out var serviceAttr) && !string.IsNullOrEmpty(serviceAttr.N) ? ParseDouble(serviceAttr.N) : (double?)null,
            EnvironmentAvg = item.TryGetValue("environmentAvg", out var envAttr) && !string.IsNullOrEmpty(envAttr.N) ? ParseDouble(envAttr.N) : (double?)null,
            District = item.TryGetValue("district", out var districtAttr) ? districtAttr.S : null,
            ThumbnailUrl = item.TryGetValue("thumbnailUrl", out var thumbnailAttr) ? thumbnailAttr.S : null
        };

        if (item.TryGetValue("photos", out var photosAttr) && photosAttr.L != null)
        {
            document.Photos = photosAttr.L
                .Where(value => !string.IsNullOrEmpty(value.S))
                .Select(value => value.S!)
                .ToList();
        }
        else
        {
            document.Photos = new List<string>();
        }

        if (item.TryGetValue("parentCenter", out var parentAttr) && parentAttr.M != null && parentAttr.M.Count > 0)
        {
            var parent = parentAttr.M;
            if (parent.TryGetValue("id", out var parentIdAttr) && !string.IsNullOrEmpty(parentIdAttr.S))
            {
                document.ParentCenter = new CenterSummaryDocument
                {
                    Id = parentIdAttr.S,
                    Name = parent.TryGetValue("name", out var parentNameAttr) ? parentNameAttr.S ?? string.Empty : string.Empty,
                    ThumbnailUrl = parent.TryGetValue("thumbnailUrl", out var parentThumbAttr) ? parentThumbAttr.S ?? string.Empty : string.Empty
                };
            }
        }

        return document;
    }

    private async Task UpdateSpotAggregatesAsync(Review review, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var getResponse = await _dynamoDb.GetItemAsync(new GetItemRequest
            {
                TableName = _spotTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = review.SpotId }
                },
                ConsistentRead = true
            }, cancellationToken);

            if (!getResponse.IsItemSet || getResponse.Item.Count == 0)
            {
                throw new InvalidOperationException($"Spot {review.SpotId} not found while updating review aggregates.");
            }

            var item = getResponse.Item;
            var currentCount = item.TryGetValue("reviewCount", out var countAttr)
                ? int.Parse(countAttr.N, CultureInfo.InvariantCulture)
                : 0;
            var currentRatingSum = item.TryGetValue("ratingSum", out var ratingSumAttr)
                ? ParseDouble(ratingSumAttr.N)
                : 0d;
            var currentTasteSum = item.TryGetValue("tasteSum", out var tasteSumAttr)
                ? ParseDouble(tasteSumAttr.N)
                : 0d;
            var currentEnvironmentSum = item.TryGetValue("environmentSum", out var environmentSumAttr)
                ? ParseDouble(environmentSumAttr.N)
                : 0d;
            var currentServiceSum = item.TryGetValue("serviceSum", out var serviceSumAttr)
                ? ParseDouble(serviceSumAttr.N)
                : 0d;

            var newCount = currentCount + 1;
            var newRatingSum = currentRatingSum + review.Rating;
            var newTasteSum = currentTasteSum + review.TasteRating;
            var newEnvironmentSum = currentEnvironmentSum + review.EnvironmentRating;
            var newServiceSum = currentServiceSum + review.ServiceRating;

            var expressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":reviewCount"] = new AttributeValue { N = newCount.ToString(CultureInfo.InvariantCulture) },
                [":ratingSum"] = new AttributeValue { N = FormatSum(newRatingSum) },
                [":tasteSum"] = new AttributeValue { N = FormatSum(newTasteSum) },
                [":environmentSum"] = new AttributeValue { N = FormatSum(newEnvironmentSum) },
                [":serviceSum"] = new AttributeValue { N = FormatSum(newServiceSum) },
                [":rating"] = new AttributeValue { N = FormatAverage(CalculateAverage(newRatingSum, newCount)) },
                [":tasteAvg"] = new AttributeValue { N = FormatAverage(CalculateAverage(newTasteSum, newCount)) },
                [":environmentAvg"] = new AttributeValue { N = FormatAverage(CalculateAverage(newEnvironmentSum, newCount)) },
                [":serviceAvg"] = new AttributeValue { N = FormatAverage(CalculateAverage(newServiceSum, newCount)) },
                [":now"] = new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) }
            };

            string conditionExpression;
            if (item.ContainsKey("reviewCount"))
            {
                conditionExpression = "reviewCount = :expectedReviewCount";
                expressionAttributeValues[":expectedReviewCount"] = new AttributeValue
                {
                    N = currentCount.ToString(CultureInfo.InvariantCulture)
                };
            }
            else
            {
                conditionExpression = "attribute_not_exists(reviewCount)";
            }

            try
            {
                await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
                {
                    TableName = _spotTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["id"] = new AttributeValue { S = review.SpotId }
                    },
                    UpdateExpression = "SET reviewCount = :reviewCount, ratingSum = :ratingSum, tasteSum = :tasteSum, environmentSum = :environmentSum, serviceSum = :serviceSum, rating = :rating, tasteAvg = :tasteAvg, environmentAvg = :environmentAvg, serviceAvg = :serviceAvg, lastReviewAt = :now",
                    ExpressionAttributeValues = expressionAttributeValues,
                    ConditionExpression = conditionExpression
                }, cancellationToken);
                return;
            }
            catch (ConditionalCheckFailedException)
            {
                _logger.LogWarning("Spot aggregate update conflicted for {SpotId}; retry attempt {Attempt}.", review.SpotId, attempt);
            }
        }

        throw new InvalidOperationException($"Unable to update aggregates for spot {review.SpotId} after multiple attempts.");
    }

    private static string FormatAverage(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatSum(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static double CalculateAverage(double sum, int count) =>
        count == 0 ? 0d : Math.Round(sum / count, 2, MidpointRounding.AwayFromZero);

    private static double ParseDouble(string raw) =>
        double.Parse(raw, CultureInfo.InvariantCulture);

    private static double TryParseDouble(Dictionary<string, AttributeValue> item, string attributeName, double fallback)
    {
        if (item.TryGetValue(attributeName, out var value) && !string.IsNullOrEmpty(value.N))
        {
            return ParseDouble(value.N);
        }

        return fallback;
    }
}
