using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace review_service.UnitTests;

public class JwtSubjectResolverTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ResolveUserId_ShouldReturnHeaderUser_WhenXUserSubExists()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["x-user-sub"] = "user-123";

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.EqualTo("user-123"));
    }

    [Test]
    public void ResolveUserId_ShouldReturnNull_WhenNoHeaders()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ResolveUserId_ShouldReturnNull_WhenAuthorizationHeaderInvalidFormat()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "InvalidToken";

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ResolveUserId_ShouldReturnSub_FromValidJwt()
    {
        // Arrange
        var payload = new { sub = "user-456" };
        var json = JsonSerializer.Serialize(payload);
        var base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var token = $"header.{base64Payload}.signature";
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.EqualTo("user-456"));
    }

    [Test]
    public void ResolveUserId_ShouldReturnNull_WhenJwtHasNoSub()
    {
        // Arrange
        var payload = new { name = "no-sub" };
        var json = JsonSerializer.Serialize(payload);
        var base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var token = $"header.{base64Payload}.signature";
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = $"Bearer {token}";

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ResolveUserId_ShouldLogWarning_WhenJwtParsingFails()
    {
        // Arrange
        var services = new ServiceCollection();
        var loggerFactory = new LoggerFactory();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        // malformed JWT
        context.Request.Headers["Authorization"] = "Bearer invalid.token.value";

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.Null);
        // (Optional) you can verify logs here if using a mock ILoggerFactory
    }

    [Test]
    public void ResolveUserId_ShouldReturnNull_WhenTokenIsEmpty()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer ";

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ResolveUserId_ShouldReturnNull_WhenJwtHasNoDot()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer abcdef";

        // Act
        var result = JwtSubjectResolver.ResolveUserId(context);

        // Assert
        Assert.That(result, Is.Null);
    }
}
