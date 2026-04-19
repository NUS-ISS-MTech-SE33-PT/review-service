using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<ReviewRepository>();
builder.Services.Configure<SpotSubmissionStorageOptions>(builder.Configuration.GetSection("SpotSubmissionStorage"));
builder.Services.AddScoped<ReviewPhotoValidationService>();
builder.Services.AddOptions<JwtValidationOptions>()
    .Bind(builder.Configuration.GetSection(JwtValidationOptions.SectionName));
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IConfigureOptions<JwtBearerOptions>,
    ConfigureJwtBearerOptions>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();

IResult ApiError(HttpContext context, int statusCode, string code, string message)
{
    return Results.Json(new
    {
        code,
        message,
        traceId = context.TraceIdentifier
    }, statusCode: statusCode);
}

static string SanitizeHeaders(IHeaderDictionary headers)
{
    var maskedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "x-user-sub"
    };

    return string.Join("; ", headers.Select(header =>
    {
        var value = maskedHeaders.Contains(header.Key) ? "***" : header.Value.ToString();
        return $"{header.Key}: {value}";
    }));
}

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        app.Logger.LogError(exception, "Unhandled exception while processing request.");

        var message = app.Environment.IsDevelopment()
            ? (exception?.Message ?? "Unhandled server error.")
            : "An unexpected error occurred.";

        var result = ApiError(context, StatusCodes.Status500InternalServerError, "internal_error", message);
        await result.ExecuteAsync(context);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["Strict-Transport-Security"] =
            "max-age=31536000; includeSubDomains";
        await next.Invoke();
    });
}

var logger = app.Logger;
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var utcNow = DateTime.UtcNow.ToString("o");
    var method = context.Request.Method;
    var path = context.Request.Path;
    var headers = SanitizeHeaders(context.Request.Headers);

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
    async (string id, CreateReviewRequest request, HttpContext ctx, ReviewRepository repo, ReviewPhotoValidationService photoValidationService) =>
{
    var userId = JwtSubjectResolver.ResolveUserId(ctx);
    if (string.IsNullOrEmpty(userId))
    {
        return ApiError(ctx, StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.");
    }

    if (string.IsNullOrWhiteSpace(request.SpotId))
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "spotId is required.");
    }

    if (!string.Equals(request.SpotId, id, StringComparison.Ordinal))
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "spotId in body must match route id.");
    }

    static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        if (value < 0) return 0;
        if (value > 5) return 5;
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    static double ClampPrice(double value, double maxAllowed)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        if (value < 0) return 0;
        if (value > maxAllowed) return maxAllowed;
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    var taste = Clamp(request.TasteRating);
    var environment = Clamp(request.EnvironmentRating);
    var service = Clamp(request.ServiceRating);

    if (taste <= 0 || environment <= 0 || service <= 0)
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "Taste, environment, and service ratings must be greater than zero.");
    }

    var rawOverall = Clamp(request.Rating);
    var overall = rawOverall > 0
        ? rawOverall
        : Math.Round((taste + environment + service) / 3d, 2, MidpointRounding.AwayFromZero);
    var priceCap = app.Configuration.GetValue<double?>("ReviewPrice:Max") ?? 1000d;
    var price = ClampPrice(request.PricePerPerson, priceCap);

    if (price <= 0)
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "pricePerPerson must be greater than zero.");
    }

    var photoUrls = request.PhotoUrls?.Select(url => url?.Trim() ?? string.Empty).ToArray() ?? [];
    var photoStorageKeys = request.PhotoStorageKeys?.Select(key => key?.Trim() ?? string.Empty).ToArray() ?? [];

    if (photoUrls.Length != photoStorageKeys.Length)
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "photoUrls and photoStorageKeys must have the same length.");
    }

    if (photoUrls.Any(string.IsNullOrWhiteSpace) || photoStorageKeys.Any(string.IsNullOrWhiteSpace))
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "photoUrls and photoStorageKeys cannot contain empty values.");
    }

    try
    {
        for (var i = 0; i < photoStorageKeys.Length; i++)
        {
            if (!photoValidationService.IsOwnedByUser(photoStorageKeys[i], photoUrls[i], userId))
            {
                return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "photoUrls/photoStorageKeys must belong to the authenticated user.");
            }

            await photoValidationService.ValidateUploadedObjectAsync(
                photoStorageKeys[i],
                photoUrls[i],
                ctx.RequestAborted);
        }
    }
    catch (ArgumentException ex)
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", ex.Message);
    }

    var review = new Review
    {
        SpotId = request.SpotId,
        UserId = userId,
        Rating = overall,
        TasteRating = taste,
        EnvironmentRating = environment,
        ServiceRating = service,
        PricePerPerson = price,
        Text = request.Text,
        PhotoUrls = photoUrls.Length == 0 ? null : photoUrls
    };

    await repo.SaveAsync(review);

    return Results.Ok(new CreateReviewResponse { Id = review.Id });
}).RequireAuthorization();

// GET /users/me/reviews
app.MapGet("/users/me/reviews", async (HttpContext ctx, ReviewRepository repo) =>
{
    var userId = JwtSubjectResolver.ResolveUserId(ctx);
    if (string.IsNullOrEmpty(userId))
    {
        return ApiError(ctx, StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.");
    }

    var reviews = await repo.GetByUserIdOrderByCreatedAtDescendingAsync(userId);
    return Results.Ok(new GetReviewsResponse { Items = reviews });
}).RequireAuthorization();

// GET /reviews/recent
app.MapGet("/reviews/recent", async (int? limit, ReviewRepository repo) =>
{
    var safeLimit = Math.Clamp(limit.GetValueOrDefault(20), 1, 50);
    var items = await repo.GetRecentReviewsWithSpotAsync(safeLimit);
    return Results.Ok(new GetRecentReviewsResponse { Items = items });
});

// GET /spots/{id}/favorite
app.MapGet("/spots/{id}/favorite", async (string id, HttpContext ctx, ReviewRepository repo) =>
{
    var userId = JwtSubjectResolver.ResolveUserId(ctx);
    if (string.IsNullOrEmpty(userId))
    {
        return ApiError(ctx, StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.");
    }
    if (string.IsNullOrWhiteSpace(id))
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "Spot id is required.");
    }

    var isFavorite = await repo.IsFavoriteAsync(userId, id);
    return Results.Ok(new { spotId = id, isFavorite });
}).RequireAuthorization();

// PUT /spots/{id}/favorite
app.MapPut("/spots/{id}/favorite", async (string id, HttpContext ctx, ReviewRepository repo) =>
{
    var userId = JwtSubjectResolver.ResolveUserId(ctx);
    if (string.IsNullOrEmpty(userId))
    {
        return ApiError(ctx, StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.");
    }
    if (string.IsNullOrWhiteSpace(id))
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "Spot id is required.");
    }

    await repo.AddFavoriteAsync(userId, id);
    return Results.NoContent();
}).RequireAuthorization();

// DELETE /spots/{id}/favorite
app.MapDelete("/spots/{id}/favorite", async (string id, HttpContext ctx, ReviewRepository repo) =>
{
    var userId = JwtSubjectResolver.ResolveUserId(ctx);
    if (string.IsNullOrEmpty(userId))
    {
        return ApiError(ctx, StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.");
    }
    if (string.IsNullOrWhiteSpace(id))
    {
        return ApiError(ctx, StatusCodes.Status400BadRequest, "validation_error", "Spot id is required.");
    }

    await repo.RemoveFavoriteAsync(userId, id);
    return Results.NoContent();
}).RequireAuthorization();

// GET /users/me/favorites
app.MapGet("/users/me/favorites", async (HttpContext ctx, ReviewRepository repo) =>
{
    var userId = JwtSubjectResolver.ResolveUserId(ctx);
    if (string.IsNullOrEmpty(userId))
    {
        return ApiError(ctx, StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.");
    }

    var items = await repo.GetFavoritesWithSpotAsync(userId);
    return Results.Ok(new GetFavoritesResponse { Items = items });
}).RequireAuthorization();

app.Run();

public partial class Program { }
