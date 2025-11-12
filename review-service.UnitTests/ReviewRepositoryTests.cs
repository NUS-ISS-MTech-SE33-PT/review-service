using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

[TestFixture]
public class ReviewRepositoryTests
{
    private Mock<IAmazonDynamoDB> _dynamoMock;
    private Mock<ILogger<ReviewRepository>> _loggerMock;
    private Mock<IConfiguration> _configMock;
    private ReviewRepository _repository;

    [SetUp]
    public void Setup()
    {
        _dynamoMock = new Mock<IAmazonDynamoDB>();
        _loggerMock = new Mock<ILogger<ReviewRepository>>();
        _configMock = new Mock<IConfiguration>();

        _configMock.Setup(c => c["DynamoDb"]).Returns("ReviewTable");
        _configMock.Setup(c => c["SpotDynamoDb"]).Returns("SpotTable");
        _configMock.Setup(c => c["FavoriteDynamoDb"]).Returns("FavoriteTable");

        _repository = new ReviewRepository(_dynamoMock.Object, _configMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task SaveAsync_ShouldCallPutItemAndUpdateSpotAggregates()
    {
        // Arrange
        var review = new Review
        {
            Id = "r1",
            SpotId = "s1",
            UserId = "u1",
            Rating = 4.5,
            TasteRating = 4.0,
            EnvironmentRating = 5.0,
            ServiceRating = 4.0,
            PricePerPerson = 15,
            Text = "Nice place!",
            CreatedAt = DateTime.UtcNow,
            PhotoUrls = new[] { "photo1.jpg" }
        };

        _dynamoMock.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse())
            .Verifiable();

        _dynamoMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["reviewCount"] = new AttributeValue { N = "5" },
                    ["ratingSum"] = new AttributeValue { N = "20" },
                    ["tasteSum"] = new AttributeValue { N = "19" },
                    ["environmentSum"] = new AttributeValue { N = "21" },
                    ["serviceSum"] = new AttributeValue { N = "18" },
                    ["priceSum"] = new AttributeValue { N = "60" },
                }
            });

        _dynamoMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateItemResponse())
            .Verifiable();

        // Act
        await _repository.SaveAsync(review);

        // Assert
        _dynamoMock.Verify(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _dynamoMock.Verify(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetBySpotIdOrderByCreatedAtDescendingAsync_ShouldReturnMappedReviews()
    {
        // Arrange
        var spotId = "spot1";
        _dynamoMock.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        ["id"] = new AttributeValue { S = "r1" },
                        ["spotId"] = new AttributeValue { S = spotId },
                        ["userId"] = new AttributeValue { S = "u1" },
                        ["rating"] = new AttributeValue { N = "4.5" },
                        ["text"] = new AttributeValue { S = "Good food" },
                        ["createdAt"] = new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
                    }
                }
            });

        // Act
        var result = await _repository.GetBySpotIdOrderByCreatedAtDescendingAsync(spotId);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("r1"));
        Assert.That(result[0].Text, Is.EqualTo("Good food"));
    }

    [Test]
    public async Task IsFavoriteAsync_ShouldReturnTrue_WhenItemExists()
    {
        // Arrange
        _dynamoMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue> { ["spotId"] = new AttributeValue { S = "s1" } }
            });

        // Act
        var result = await _repository.IsFavoriteAsync("user1", "spot1");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsFavoriteAsync_ShouldReturnFalse_WhenItemEmpty()
    {
        // Arrange
        _dynamoMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

        // Act
        var result = await _repository.IsFavoriteAsync("user1", "spot1");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetByUserIdOrderByCreatedAtDescendingAsync_ShouldReturnMappedReviews()
    {
        // Arrange
        var userId = "user1";

        _dynamoMock.Setup(d => d.QueryAsync(It.Is<QueryRequest>(r =>
                r.TableName == "ReviewTable" &&
                r.IndexName == "reviews_by_user" &&
                r.ExpressionAttributeValues[":userId"].S == userId &&
                r.ScanIndexForward == false
            ),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                new()
                {
                    ["id"] = new AttributeValue { S = "r1" },
                    ["spotId"] = new AttributeValue { S = "spot1" },
                    ["userId"] = new AttributeValue { S = userId },
                    ["rating"] = new AttributeValue { N = "4.5" },
                    ["tasteRating"] = new AttributeValue { N = "4.0" },
                    ["environmentRating"] = new AttributeValue { N = "5.0" },
                    ["serviceRating"] = new AttributeValue { N = "3.5" },
                    ["pricePerPerson"] = new AttributeValue { N = "12.0" },
                    ["text"] = new AttributeValue { S = "Great food!" },
                    ["createdAt"] = new AttributeValue
                    {
                        N = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
                    }
                }
                }
            });

        // Act
        var result = await _repository.GetByUserIdOrderByCreatedAtDescendingAsync(userId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        var review = result[0];
        Assert.That(review.Id, Is.EqualTo("r1"));
        Assert.That(review.SpotId, Is.EqualTo("spot1"));
        Assert.That(review.UserId, Is.EqualTo(userId));
        Assert.That(review.Rating, Is.EqualTo(4.5));
        Assert.That(review.TasteRating, Is.EqualTo(4.0));
        Assert.That(review.EnvironmentRating, Is.EqualTo(5.0));
        Assert.That(review.ServiceRating, Is.EqualTo(3.5));
        Assert.That(review.PricePerPerson, Is.EqualTo(12.0));
        Assert.That(review.Text, Is.EqualTo("Great food!"));
    }

    [Test]
    public async Task GetRecentReviewsWithSpotAsync_ShouldReturnEmpty_WhenLimitIsZero()
    {
        var result = await _repository.GetRecentReviewsWithSpotAsync(0);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetRecentReviewsWithSpotAsync_ShouldReturnEmpty_WhenNoReviews()
    {
        _dynamoMock.Setup(d => d.ScanAsync(It.IsAny<ScanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanResponse { Items = new List<Dictionary<string, AttributeValue>>() });

        var result = await _repository.GetRecentReviewsWithSpotAsync(5);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetFavoritesWithSpotAsync_ShouldReturnEmpty_WhenNoFavorites()
    {
        _dynamoMock.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = new List<Dictionary<string, AttributeValue>>() });

        var result = await _repository.GetFavoritesWithSpotAsync("user1");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetFavoritesWithSpotAsync_ShouldReturnEmpty_WhenSpotIdMissing()
    {
        _dynamoMock.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new() { ["someOtherField"] = new AttributeValue { S = "value" } }
                }
            });

        var result = await _repository.GetFavoritesWithSpotAsync("user1");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetFavoritesWithSpotAsync_ShouldReturnMappedFavorites_WithNullSpot()
    {
        // Favorite item with spotId
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        _dynamoMock.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        ["spotId"] = new AttributeValue { S = "s1" },
                        ["createdAt"] = new AttributeValue { N = createdAtMs }
                    }
                }
            });

        _dynamoMock.Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchGetItemResponse
            {
                Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>()
            });

        // We cannot mock FetchSpotsAsync because it's private, so Spot will remain null
        var result = await _repository.GetFavoritesWithSpotAsync("user1");

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].SpotId, Is.EqualTo("s1"));
        Assert.That(result[0].Spot, Is.Null);
        Assert.That(result[0].AddedAt, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(createdAtMs)).UtcDateTime));
    }

    [Test]
    public async Task GetRecentReviewsWithSpotAsync_ShouldReturnReviewsWithSpotMap()
    {
        // 1. Mock ScanAsync to return a review item
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        _dynamoMock.Setup(d => d.ScanAsync(It.IsAny<ScanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new Dictionary<string, AttributeValue>
                    {
                        ["id"] = new AttributeValue { S = "review1" },
                        ["spotId"] = new AttributeValue { S = "spot1" },
                        ["userId"] = new AttributeValue { S = "user1" },
                        ["rating"] = new AttributeValue { N = "4.5" },
                        ["tasteRating"] = new AttributeValue { N = "4" },
                        ["environmentRating"] = new AttributeValue { N = "5" },
                        ["serviceRating"] = new AttributeValue { N = "4" },
                        ["pricePerPerson"] = new AttributeValue { N = "10" },
                        ["text"] = new AttributeValue { S = "Great!" },
                        ["createdAt"] = new AttributeValue { N = createdAtMs }
                    }
                }
            });

        // 2. Mock BatchGetItemAsync to return empty, so Spot will be null
        _dynamoMock.Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchGetItemResponse
            {
                Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>()
            });

        // 3. Call the method under test
        var result = await _repository.GetRecentReviewsWithSpotAsync(5);

        // 4. Assert
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Review.Id, Is.EqualTo("review1"));
        Assert.That(result[0].Spot, Is.Null); // Spot is null because FetchSpotsAsync returned empty
        Assert.That(result[0].Review.Text, Is.EqualTo("Great!"));
    }

    [Test]
    public async Task AddFavoriteAsync_ShouldCallPutItemAsync()
    {
        // Arrange
        var userId = "user1";
        var spotId = "spot1";

        _dynamoMock.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse()); // simulate successful put

        // Act
        await _repository.AddFavoriteAsync(userId, spotId);

        // Assert
        _dynamoMock.Verify(d => d.PutItemAsync(
            It.Is<PutItemRequest>(req =>
                req.TableName == "FavoriteTable" &&
                req.Item["userId"].S == userId &&
                req.Item["spotId"].S == spotId
            ),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Test]
    public async Task RemoveFavoriteAsync_ShouldCallDeleteItemAsync()
    {
        // Arrange
        var userId = "user1";
        var spotId = "spot1";

        _dynamoMock.Setup(d => d.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteItemResponse()); // simulate successful delete

        // Act
        await _repository.RemoveFavoriteAsync(userId, spotId);

        // Assert
        _dynamoMock.Verify(d => d.DeleteItemAsync(
            It.Is<DeleteItemRequest>(req =>
                req.TableName == "FavoriteTable" &&
                req.Key["userId"].S == userId &&
                req.Key["spotId"].S == spotId
            ),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Test]
    public async Task GetFavoritesWithSpotAsync_ShouldMapFavoritesWithSpot()
    {
        // Arrange
        var userId = "user1";

        // 1. Mock QueryAsync to return a favorite item
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        _dynamoMock.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                new Dictionary<string, AttributeValue>
                {
                    ["spotId"] = new AttributeValue { S = "spot1" },
                    ["userId"] = new AttributeValue { S = userId },
                    ["createdAt"] = new AttributeValue { N = createdAtMs }
                }
                }
            });

        // 2. Mock BatchGetItemAsync so that FetchSpotsAsync returns a spot
        _dynamoMock.Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchGetItemResponse
            {
                Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
                {
                    ["SpotTable"] = new List<Dictionary<string, AttributeValue>>
                    {
                    new Dictionary<string, AttributeValue>
                    {
                        ["id"] = new AttributeValue { S = "spot1" },
                        ["name"] = new AttributeValue { S = "My Spot" },
                        ["address"] = new AttributeValue { S = "123 Main St" },
                        ["rating"] = new AttributeValue { N = "4.5" }
                    }
                    }
                }
            });

        // Act
        var result = await _repository.GetFavoritesWithSpotAsync(userId);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        var favorite = result[0];
        Assert.That(favorite.SpotId, Is.EqualTo("spot1"));
        Assert.That(favorite.Spot, Is.Not.Null);
        Assert.That(favorite.Spot!.Name, Is.EqualTo("My Spot"));

        // Verify DynamoDB calls
        _dynamoMock.Verify(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _dynamoMock.Verify(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetFavoritesWithSpotAsync_ShouldHandleUnprocessedKeys()
    {
        // Arrange
        var userId = "user1";

        // 1. Mock QueryAsync to return a favorite item
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        _dynamoMock.Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                new Dictionary<string, AttributeValue>
                {
                    ["spotId"] = new AttributeValue { S = "spot1" },
                    ["userId"] = new AttributeValue { S = userId },
                    ["createdAt"] = new AttributeValue { N = createdAtMs }
                }
                }
            });

        // 2. Mock BatchGetItemAsync with unprocessed keys on first call
        var callCount = 0;
        _dynamoMock.Setup(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call: return empty response and unprocessed keys
                    return new BatchGetItemResponse
                    {
                        Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>(),
                        UnprocessedKeys = new Dictionary<string, KeysAndAttributes>
                        {
                            ["SpotTable"] = new KeysAndAttributes
                            {
                                Keys = new List<Dictionary<string, AttributeValue>>
                                {
                                new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = "spot1" } }
                                }
                            }
                        }
                    };
                }
                else
                {
                    // Second call: return proper response and empty unprocessed keys
                    return new BatchGetItemResponse
                    {
                        Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
                        {
                            ["SpotTable"] = new List<Dictionary<string, AttributeValue>>
                            {
                            new Dictionary<string, AttributeValue>
                            {
                                ["id"] = new AttributeValue { S = "spot1" },
                                ["name"] = new AttributeValue { S = "My Spot" },
                                ["address"] = new AttributeValue { S = "123 Main St" },
                                ["rating"] = new AttributeValue { N = "4.5" }
                            }
                            }
                        },
                        UnprocessedKeys = new Dictionary<string, KeysAndAttributes>()
                    };
                }
            });

        // Act
        var result = await _repository.GetFavoritesWithSpotAsync(userId);

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        var favorite = result[0];
        Assert.That(favorite.SpotId, Is.EqualTo("spot1"));
        Assert.That(favorite.Spot, Is.Not.Null);
        Assert.That(favorite.Spot!.Name, Is.EqualTo("My Spot"));

        // Verify BatchGetItemAsync called at least twice
        _dynamoMock.Verify(d => d.BatchGetItemAsync(It.IsAny<BatchGetItemRequest>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

}