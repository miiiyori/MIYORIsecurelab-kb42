using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class LoginRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Login_RateLimiting_RejectsOn11thTryAndRecoversAfterWindow()
    {
        // окремий host, щоб наші 11 запитів не вичерпали ліміт для інших тестів
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var wrongLogin = new { userName = "alice", password = "WrongPassword1!" };
        
        // робимо 10 невдалих спроб, щоб вичерпати вікно. очікуємо 401
        for (int i = 0; i < 10; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", wrongLogin);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // 11-й запит має відхилитися Rate Limiter-ом з кодом 429
        using var rejectedResponse = await client.PostAsJsonAsync("/api/auth/login", wrongLogin);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);

        // перевіряємо, що в тілі 429 помилки немає зливів даних (імені чи пароля)
        var rejectedContent = await rejectedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("alice", rejectedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", rejectedContent, StringComparison.OrdinalIgnoreCase);

        // чекаємо завершення 10-секундного вікна (+0.5с для певності)
        await Task.Delay(TimeSpan.FromSeconds(10.5));

        // перевіряємо відновлення: правильний вхід після вікна має працювати
        var correctLogin = new { userName = "alice", password = StudyPassword };
        using var recoveredResponse = await client.PostAsJsonAsync("/api/auth/login", correctLogin);

        Assert.Equal(HttpStatusCode.NoContent, recoveredResponse.StatusCode);
    }
}

