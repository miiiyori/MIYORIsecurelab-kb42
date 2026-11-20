using System.Net;

namespace SecureLab.Api.Tests;

// перевірка ліміту пошуку: 10 запитів на 10 секунд для кожного користувача окремо, без черги
public sealed class SearchRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Search_RateLimit_Rejects11th_IsolatesPartitions_AndRecoversAfterWindow()
    {
        // окремий host: власні лічильники, щоб тест не вичерпав ліміт для інших тестів
        using var host = factory.WithWebHostBuilder(_ => { });
        using var alice = await host.LoginAsync("alice", SeedPassword);
        using var bob = await host.LoginAsync("bob", SeedPassword);

        // 1) у межах ліміту звичайна поведінка не ламається
        for (var i = 0; i < 10; i++)
        {
            using var ok = await alice.GetAsync("/api/incidents/search?q=a");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // 2) 11-й запит перевищує ліміт і має повернути 429 без даних інцидентів у тілі
        using var rejected = await alice.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var rejectedBody = await rejected.Content.ReadAsStringAsync();
        Assert.DoesNotContain("alice", rejectedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("description", rejectedBody, StringComparison.OrdinalIgnoreCase);

        // 3) ліміт рахується окремо по користувачу, тому запит від bob має пройти, поки alice заблокована
        using var bobResponse = await bob.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.OK, bobResponse.StatusCode);

        // 4) відновлення після вікна
        await Task.Delay(TimeSpan.FromSeconds(10.5));
        using var recovered = await alice.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    // rate limit не замінює authentication: анонім отримує 401, а не 429 і не дані
    [Fact]
    public async Task Search_Anonymous_StillGets401_NotData()
    {
        using var anonymous = factory.CreateClient();
        using var response = await anonymous.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
