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

// GET /reviews/health
app.MapGet("/reviews/health", () => Results.Ok(DateTime.Now));

// GET /spots/{id}/reviews
app.MapGet("/spots/{id}/reviews", async (string id, ReviewRepository repo) =>
{
    var reviews = await repo.GetBySpotIdOrderByCreatedAtDescendingAsync(id);
    return Results.Ok(new GetReviewsResponse { Items = reviews });
});

// POST /spots/{id}/reviews (requires auth)
app.MapPost("/spots/{id}/reviews",
    async (string id, CreateReviewRequest request, HttpContext ctx, ReviewRepository repo) =>
{
    var userId = ctx.Request.Headers["x-user-sub"].FirstOrDefault();
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.SpotId))
    {
        return Results.BadRequest(new { message = "spotId is required." });
    }

    if (!string.Equals(request.SpotId, id, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "spotId in body must match route id." });
    }

    static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        if (value < 0) return 0;
        if (value > 5) return 5;
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    var taste = Clamp(request.TasteRating);
    var environment = Clamp(request.EnvironmentRating);
    var service = Clamp(request.ServiceRating);

    if (taste <= 0 || environment <= 0 || service <= 0)
    {
        return Results.BadRequest(new { message = "Taste, environment, and service ratings must be greater than zero." });
    }

    var rawOverall = Clamp(request.Rating);
    var overall = rawOverall > 0
        ? rawOverall
        : Math.Round((taste + environment + service) / 3d, 2, MidpointRounding.AwayFromZero);

    var review = new Review
    {
        SpotId = request.SpotId,
        UserId = userId,
        Rating = overall,
        TasteRating = taste,
        EnvironmentRating = environment,
        ServiceRating = service,
        Text = request.Text,
        PhotoUrls = request.PhotoUrls
    };

    await repo.SaveAsync(review);

    return Results.Ok(new CreateReviewResponse { Id = review.Id });
});

// GET /users/me/reviews
app.MapGet("/users/me/reviews", async (HttpContext ctx, ReviewRepository repo) =>
{
    var userId = ctx.Request.Headers["x-user-sub"].FirstOrDefault();
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var reviews = await repo.GetByUserIdOrderByCreatedAtDescendingAsync(userId);
    return Results.Ok(new GetReviewsResponse { Items = reviews });
});

app.Run();
