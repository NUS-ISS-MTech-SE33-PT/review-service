using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

public class ReviewRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _reviewTableName;
    private readonly string _spotTableName;
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
