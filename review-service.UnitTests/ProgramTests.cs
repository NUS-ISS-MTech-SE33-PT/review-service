using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Moq;

[TestFixture]
public class ProgramTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private Mock<IAmazonDynamoDB> _dynamoDbMock = null!;
    private Mock<IAmazonS3> _s3Mock = null!;
    private const string Issuer = "https://tests.example.com/review-service";
    private const string AllowedClientId = "review-client";
    private const string SecondaryAllowedClientId = "review-client-admin";
    private const string SigningKey = "review-service-test-signing-key-123456";

    [SetUp]
    public void SetUp()
    {
        _dynamoDbMock = new Mock<IAmazonDynamoDB>();
        _s3Mock = new Mock<IAmazonS3>();
        _dynamoDbMock
            .Setup(dynamoDb => dynamoDb.QueryAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>()
            });
        _dynamoDbMock
            .Setup(dynamoDb => dynamoDb.PutItemAsync(
                It.IsAny<PutItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());
        _dynamoDbMock
            .Setup(dynamoDb => dynamoDb.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateItemResponse());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"{JwtValidationOptions.SectionName}:Issuer"] = Issuer,
                        [$"{JwtValidationOptions.SectionName}:AllowedClientIds:0"] = AllowedClientId,
                        [$"{JwtValidationOptions.SectionName}:AllowedClientIds:1"] = SecondaryAllowedClientId,
                        [$"{JwtValidationOptions.SectionName}:SigningKey"] = SigningKey,
                        ["DynamoDb"] = "reviews-test",
                        ["SpotDynamoDb"] = "spots-test",
                        ["FavoriteDynamoDb"] = "favorites-test",
                        ["SpotSubmissionStorage:BucketName"] = "test-bucket",
                        ["SpotSubmissionStorage:KeyPrefix"] = "submissions/",
                        ["SpotSubmissionStorage:PublicBaseUrl"] = "https://cdn.example.com"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.PostConfigure<JwtValidationOptions>(options =>
                    {
                        options.Issuer = Issuer;
                        options.AllowedClientIds = [AllowedClientId, SecondaryAllowedClientId];
                        options.SigningKey = SigningKey;
                    });
                    services.AddScoped(_ => new ReviewRepository(
                        _dynamoDbMock.Object,
                        new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["DynamoDb"] = "reviews-test",
                                ["SpotDynamoDb"] = "spots-test",
                                ["FavoriteDynamoDb"] = "favorites-test",
                                ["SpotSubmissionStorage:BucketName"] = "test-bucket",
                                ["SpotSubmissionStorage:KeyPrefix"] = "submissions/",
                                ["SpotSubmissionStorage:PublicBaseUrl"] = "https://cdn.example.com"
                            })
                            .Build(),
                        Mock.Of<ILogger<ReviewRepository>>()));
                    services.AddScoped<IAmazonS3>(_ => _s3Mock.Object);
                });
            });
    }

    [TearDown]
    public void Teardown()
    {
        _factory.Dispose(); // Dispose to free resources
    }

    [Test]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/reviews/health");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var date = await response.Content.ReadFromJsonAsync<DateTime>();
        Assert.That(date, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public async Task MyReviews_ShouldReturnUnauthorized_WhenTokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/users/me/reviews");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task MyReviews_ShouldReturnUnauthorized_WhenClientIdIsNotAllowed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(clientId: "other-client"));

        var response = await client.GetAsync("/users/me/reviews");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task MyReviews_ShouldReturnUnauthorized_WhenTokenUseIsId()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(tokenUse: "id"));

        var response = await client.GetAsync("/users/me/reviews");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task MyReviews_ShouldReturnUnauthorized_WhenTokenIsExpired()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            CreateToken(expiresAt: DateTime.UtcNow.AddMinutes(-5)));

        var response = await client.GetAsync("/users/me/reviews");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task MyReviews_ShouldReturnUnauthorized_WhenIssuerIsWrong()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            CreateToken(issuer: "https://tests.example.com/wrong-issuer"));

        var response = await client.GetAsync("/users/me/reviews");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task MyReviews_ShouldReturnOk_WhenTokenIsValidAccessToken()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken());

        var response = await client.GetAsync("/users/me/reviews");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task MyReviews_ShouldReturnOk_WhenSecondAllowedClientIdIsUsed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            CreateToken(clientId: SecondaryAllowedClientId));

        var response = await client.GetAsync("/users/me/reviews");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PostReview_ShouldReturnBadRequest_WhenPhotoStorageKeysMissing()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken());

        var response = await client.PostAsJsonAsync("/spots/spot-1/reviews", new
        {
            spotId = "spot-1",
            rating = 4.5,
            tasteRating = 4.5,
            environmentRating = 4.5,
            serviceRating = 4.5,
            pricePerPerson = 12.5,
            text = "solid",
            photoUrls = new[]
            {
                "https://cdn.example.com/submissions/user-123/review.png"
            }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("photoUrls and photoStorageKeys must have the same length."));
    }

    [Test]
    public async Task PostReview_ShouldReturnBadRequest_WhenUploadedPhotoExceedsLimit()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken());

        var metadata = new GetObjectMetadataResponse();
        metadata.Headers.ContentType = "image/png";
        metadata.Headers.ContentLength = 5 * 1024 * 1024 + 1;
        _s3Mock
            .Setup(s3 => s3.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request =>
                    request.BucketName == "test-bucket" &&
                    request.Key == "submissions/user-123/review.png"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        var response = await client.PostAsJsonAsync("/spots/spot-1/reviews", new
        {
            spotId = "spot-1",
            rating = 4.5,
            tasteRating = 4.5,
            environmentRating = 4.5,
            serviceRating = 4.5,
            pricePerPerson = 12.5,
            text = "solid",
            photoUrls = new[]
            {
                "https://cdn.example.com/submissions/user-123/review.png"
            },
            photoStorageKeys = new[]
            {
                "submissions/user-123/review.png"
            }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("File size exceeds limit"));
    }

    private static string CreateToken(
        string subject = "user-123",
        string clientId = AllowedClientId,
        string tokenUse = "access",
        string issuer = Issuer,
        DateTime? expiresAt = null)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("client_id", clientId),
            new("token_use", tokenUse)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var notBefore = expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow
            ? expiresAt.Value.AddMinutes(-10)
            : DateTime.UtcNow.AddMinutes(-1);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: null,
            claims: claims,
            notBefore: notBefore,
            expires: expiresAt ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
