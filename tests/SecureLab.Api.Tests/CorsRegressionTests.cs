using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SecureLab.Api.Tests;

// перевіряємо, що сервер віддає cors-заголовки тільки для дозволеного origin і методу;
// сам тест перевіряє заголовки, а блокування робить уже браузер
public sealed class CorsRegressionTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string Allowed = "http://localhost:4173";
    private const string Other = "http://localhost:4174";

    private static HttpRequestMessage Simple(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static HttpRequestMessage Preflight(string origin, string method, string? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        if (headers is not null) request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    private static bool Has(HttpResponseMessage response, string name) => response.Headers.Contains(name);

    [Fact]
    public async Task Development_AllowedOrigin_GetsAllowOriginAndCredentials()
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Simple(Allowed));
        Assert.Equal(Allowed, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    [Fact]
    public async Task Development_OtherOrigin_GetsNoCorsHeaders()
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Simple(Other));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);       // сервер відповів; рішення за браузером
        Assert.False(Has(response, "Access-Control-Allow-Origin"));
        Assert.False(Has(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Development_Preflight_AllowsGetWithProbeHeader_ButNotPut_AndNotOtherOrigin()
    {
        using var client = factory.CreateClient();

        using var allowed = await client.SendAsync(Preflight(Allowed, "GET", "x-lab-cors-probe"));
        Assert.Equal(Allowed, Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("GET", string.Join(",", allowed.Headers.GetValues("Access-Control-Allow-Methods")));

        using var put = await client.SendAsync(Preflight(Allowed, "PUT"));
        Assert.DoesNotContain("PUT", put.Headers.TryGetValues("Access-Control-Allow-Methods", out var putMethods) ? string.Join(",", putMethods) : "");       // метод PUT політикою не дозволений

        using var other = await client.SendAsync(Preflight(Other, "GET", "x-lab-cors-probe"));
        Assert.False(Has(other, "Access-Control-Allow-Origin"));
    }

    // у production cors-політика не реєструється, тому навіть dev-origin 4173 не має отримувати cors-заголовків
    [Fact]
    public async Task Production_AllowedDevOrigin_GetsNoCorsHeaders()
    {
        var connectionString = factory.Services.GetRequiredService<IConfiguration>()
            .GetConnectionString("SecureLab")!;
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SecureLab", connectionString);
        });
        using var client = production.CreateClient();
        using var response = await client.SendAsync(Simple(Allowed));
        Assert.False(Has(response, "Access-Control-Allow-Origin"));
    }
}
