using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

public class ReviewRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    public ReviewRepository(IAmazonDynamoDB dynamoDb, IConfiguration configuration)
    {
        _dynamoDb = dynamoDb;
        _tableName = configuration["DynamoDb"]!;
    }

    public async Task SaveAsync(Review review)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["spotId"] = new AttributeValue { S = review.SpotId },
            ["id"] = new AttributeValue { S = review.Id },
            ["userId"] = new AttributeValue { S = review.UserId },
            ["rating"] = new AttributeValue { N = review.Rating.ToString() },
            ["text"] = new AttributeValue { S = review.Text },
            ["createdAt"] = new AttributeValue { N = ((DateTimeOffset)review.CreatedAt).ToUnixTimeMilliseconds().ToString() }
        };

        if (review.PhotoUrls != null && review.PhotoUrls.Length > 0)
        {
            item["photoUrls"] = new AttributeValue { SS = review.PhotoUrls.ToList() };
        }

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = item
        };

        await _dynamoDb.PutItemAsync(request);
    }

    public async Task<List<Review>> GetBySpotIdOrderByCreatedAtDescendingAsync(string spotId)
    {
        var request = new QueryRequest
        {
            TableName = _tableName,
            IndexName = "reviews_by_createdAt",
            KeyConditionExpression = "spotId = :spotId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":spotId"] = new AttributeValue { S = spotId }
            },
            ScanIndexForward = false
        };

        var response = await _dynamoDb.QueryAsync(request);

        return response.Items.Select(item => new Review
        {
            Id = item["id"].S,
            SpotId = item["spotId"].S,
            UserId = item["userId"].S,
            Rating = int.Parse(item["rating"].N),
            Text = item["text"].S,
            PhotoUrls = item.TryGetValue("photoUrls", out AttributeValue? value) ? value.SS.ToArray() : null,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(item["createdAt"].N)).UtcDateTime
        }).ToList();
    }
}
