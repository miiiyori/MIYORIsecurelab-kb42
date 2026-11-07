using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class CookieFlagsTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Login_SetCookie_HasHttpOnlySecureSameSiteLax()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = StudyPassword });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var header = cookies!.Single(c => c.StartsWith(".AspNetCore.Identity.Application=",
            StringComparison.Ordinal));

        // беремо лише атрибути (після першої ;), щоб значення cookie не потрапило в повідомлення про помилку
        var attributes = header[(header.IndexOf(';') + 1)..].ToLowerInvariant();

        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/", attributes);
    }
}