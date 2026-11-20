using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SecureLab.Api.Tests;

// перевіряємо, що базові заголовки безпеки є на всіх типах відповідей, включно з 500 (фікс F-02)
public sealed class SecurityHeadersTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string ExpectedCsp = "default-src 'self'; frame-ancestors 'none'";

    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static string HeaderValue(HttpResponseMessage response, string name)
    {
        var found = response.Headers.TryGetValues(name, out var values);
        Assert.True(found, $"Header {name} is missing");
        return Assert.Single(values!);
    }

    private static void AssertBaselineHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", HeaderValue(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", HeaderValue(response, "Referrer-Policy"));
        Assert.Equal(ExpectedCsp, HeaderValue(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task StaticPage_Returns200Html_WithBaselineHeaders()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        AssertBaselineHeaders(response);
    }

    [Fact]
    public async Task SuccessfulApi_ValidationError_And404_HaveBaselineHeaders()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword);

        using var success = await alice.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        AssertBaselineHeaders(success);

        using var validation = await alice.GetAsync("/api/incidents?status=Unknown");
        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        AssertBaselineHeaders(validation);

        using var notFound = await alice.GetAsync("/api/incidents/99999999-9999-9999-9999-999999999999");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        AssertBaselineHeaders(notFound);
    }

    // імітуємо 500-помилку в production через недоступну бд на порту 54399, щоб перевірити безпечну відповідь і заголовки
    [Fact]
    public async Task UnexpectedError500_InProduction_IsSafe_AndKeepsBaselineHeaders()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SecureLab",
                "Host=127.0.0.1;Port=54399;Database=securelab;Username=securelab;"
                + "Password=unreachable-study-value;Include Error Detail=false;Timeout=3");
        });
        Assert.Equal("Production",
            production.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

        using var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = "NotARealPassword1!" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("traceId", body, StringComparison.Ordinal);
        foreach (var forbidden in new[]
                 { "Npgsql", "54399", "Host=", "Password", "unreachable-study-value", "NotARealPassword1!", "   at " })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        }

        AssertBaselineHeaders(response);   // до виправлення F-02 тут падало, бо exception handler скидав заголовки
    }
}
