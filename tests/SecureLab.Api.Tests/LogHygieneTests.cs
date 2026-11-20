using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using SecureLab.Api.Scaffolding;
using SecureLab.Api.Security;

namespace SecureLab.Api.Tests;

// перевіряємо логи в пам'яті: паролі, cookie й токени туди не потрапляють, а audit-події (вхід, відмова, csrf) записуються
public sealed class LogHygieneTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string WrongPassword = "NotARealPassword1!";

    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Logs_DoNotContainCredentialsCookiesOrTokens_AndAuditEventsAreWritten()
    {
        var logs = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));

        // невдалий вхід (коректна 401, пароль не має потрапити в журнал)
        using (var anonymous = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = true }))
        {
            using var failed = await anonymous.PostAsJsonAsync("/api/auth/login",
                new { userName = "alice", password = WrongPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using var alice = await host.LoginAsync("alice", SeedPassword);            // успішний вхід
        using var morgan = await host.LoginAsync("morgan", SeedPassword);
        var aliceToken = Assert.Single(alice.DefaultRequestHeaders.GetValues(CsrfDefaults.HeaderName));
        var morganToken = Assert.Single(morgan.DefaultRequestHeaders.GetValues(CsrfDefaults.HeaderName));

        using var created = await alice.PostAsJsonAsync("/api/incidents", new
        {
            title = $"Log test {Guid.NewGuid():N}",
            description = "опис для перевірки журналу",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedIncidentResponse>())!.Id;

        try
        {
            // відмова авторизації: Reporter не може виконати triage
            using var denied = await alice.PostAsJsonAsync($"/api/incidents/{id}/triage", new { note = "спроба" });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            // дозволений привілейований перехід: Analyst
            using var allowed = await morgan.PostAsJsonAsync($"/api/incidents/{id}/triage", new { note = "ок" });
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

            // відхилений CSRF-запит: cookie є, токена немає
            using var noToken = new HttpRequestMessage(HttpMethod.Put, $"/api/incidents/{id}");
            noToken.Content = JsonContent.Create(new { title = "x" });
            using var cleanAlice = await host.LoginWithoutTokenAsync("alice", SeedPassword);
            using var csrfRejected = await cleanAlice.SendAsync(noToken);
            Assert.Equal(HttpStatusCode.BadRequest, csrfRejected.StatusCode);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }

        var entries = logs.Entries;
        Assert.NotEmpty(entries);

        // чого в журналі бути не повинно
        foreach (var secret in new[] { SeedPassword, WrongPassword, aliceToken, morganToken })
        {
            Assert.DoesNotContain(entries, line => line.Contains(secret, StringComparison.Ordinal));
        }
        Assert.DoesNotContain(entries, line => line.Contains("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, line => line.Contains("Cookie:", StringComparison.OrdinalIgnoreCase));

        // що в журналі бути повинно: audit events із subject, action, resource, result, time, trace
        Assert.Contains(entries, line => line.Contains("audit.status_transition", StringComparison.Ordinal)
            && line.Contains("result=success", StringComparison.Ordinal)
            && line.Contains("action=incident.triage", StringComparison.Ordinal)
            && line.Contains($"resource={id}", StringComparison.Ordinal));
        Assert.Contains(entries, line => line.Contains("authorization.denied", StringComparison.Ordinal)
            && line.Contains($"resource={id}", StringComparison.Ordinal));
        Assert.Contains(entries, line => line.Contains("csrf.rejected", StringComparison.Ordinal));
    }
}
