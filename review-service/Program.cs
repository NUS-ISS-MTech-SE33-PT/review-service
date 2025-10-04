using Amazon.DynamoDBv2;


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddScoped<ReviewRepository>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var logger = app.Logger;
app.Use(async (context, next) =>
{
    var utcNow = DateTime.UtcNow.ToString("o");
    var method = context.Request.Method;
    var path = context.Request.Path;
    var headers = string.Join("; ", context.Request.Headers.Select(h => $"{h.Key}: {h.Value}"));

    logger.LogInformation("{UtcNow}\t{Method}\t{Path} | Headers: {Headers}",
        utcNow, method, path, headers);

    await next.Invoke();
});

var reviews = new List<Review>();

// GET /health
app.MapGet("reviews/health", () => Results.Ok(DateTime.Now));

// GET /spots/{id}/reviews
app.MapGet("/spots/{id}/reviews", async (string id, ReviewRepository repo) =>
{
    var reviews = await repo.GetBySpotIdAsync(id);
    return Results.Ok(new GetReviewsResponse { Items = reviews });
});

// POST /spots/{id}/reviews (requires auth)
app.MapPost("/spots/{id}/reviews",
    async (string id, CreateReviewRequest request, HttpContext ctx, ReviewRepository repo) =>
{
    var userId = ctx.Request.Headers["x-user-sub"].FirstOrDefault();
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var review = new Review
    {
        SpotId = id,
        UserId = userId,
        Rating = request.Rating,
        Text = request.Text,
        PhotoUrls = request.PhotoUrls
    };

    await repo.SaveAsync(review);

    return Results.Ok(new CreateReviewResponse { Id = review.Id });
});

app.Run();