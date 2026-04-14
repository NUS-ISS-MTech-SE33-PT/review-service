using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace review_service.UnitTests;

public class JwtSubjectResolverTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ResolveUserId_ShouldReturnSubClaim_WhenPrincipalIsAuthenticated()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
            [
                new Claim("sub", "user-123")
            ], "Bearer")
        ]);

        Assert.That(JwtSubjectResolver.ResolveUserId(context), Is.EqualTo("user-123"));
    }

    [Test]
    public void ResolveUserId_ShouldReturnNull_WhenPrincipalIsAnonymous()
    {
        var context = new DefaultHttpContext();
        Assert.That(JwtSubjectResolver.ResolveUserId(context), Is.Null);
    }

    [Test]
    public void ResolveUserId_ShouldReturnNull_WhenAuthenticatedPrincipalHasNoSub()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(
        [
            new ClaimsIdentity(
            [
                new Claim("token_use", "access")
            ], "Bearer")
        ]);

        Assert.That(JwtSubjectResolver.ResolveUserId(context), Is.Null);
    }
}
