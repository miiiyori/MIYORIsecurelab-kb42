using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data; 


namespace SecureLab.Api.Tests;

public sealed class AuthorizationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    // A-01: анонімний запит до захищеного ендпоінту дає 401, а не 302 і не ХТМЛ
    [Fact]
    public async Task A01_AnonymousRequestToProtectEndpoint_Returns401_NotRedirect()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await anonymous.PostAsJsonAsync("/api/incidents", new
        {
            title = "Test A-01",
            description = "Anonymous create test",
            severity = "Low"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    // A-02: після входу /api/me повертає саме цього користувача
    [Fact]
    public async Task A02_LoginThenApiMe_ReturnsVerifiedUserAndSafeFields()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword);
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("alice", doc.RootElement.GetProperty("userName").GetString());
        Assert.False(doc.RootElement.TryGetProperty("passwordHash", out _));
    }

    // A-03: cookie alice і заголвок X-Demo_UserID боба, а /api/me лишається аліса
    [Fact]
    public async Task A03_ValidCookieWithForgedHeader_ApiMeRemainsUnchanged()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.Equal("alice", doc.RootElement.GetProperty("userName").GetString());
    }

    // A-04: створення інциденту без власника в JSON, власник береться з principal 
    [Fact]
    public async Task A04_AuthenticatedCreateWithoutOwner_SetOwnerToPrincipal()
    {
        using var client = await factory.LoginAsync("bob", StudyPassword);
        var requestBody = new
        {
            title = $"A04 {Guid.NewGuid():N}",
            description = "Test A-04 create",
            severity = "Low",
            occurredAtUtc = DateTime.UtcNow
        };
        using var response = await client.PostAsJsonAsync("/api/incidents", requestBody);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var incidentId = doc.RootElement.GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var incident = await db.Incidents.FindAsync(incidentId);
        Assert.NotNull(incident);
        Assert.Equal(DbSeeder.BobId, incident.OwnerUserId); // власником має бути боб
    }

    // A-05: боб створює інцидент із чужим ownerUserId Alice, власник лишається Bob
    [Fact]
    public async Task A05_CreateWithForgedOwnerUserId_IgnoresForgedAndSetsToPrincipal()
    {
        using var client = await factory.LoginAsync("bob", StudyPassword);
        var requestBody = new
        {
            title = $"A05 {Guid.NewGuid():N}",
            description = "Test A-05 forged owner",
            severity = "Low",
            occurredAtUtc = DateTime.UtcNow,
            ownerUserId = DbSeeder.AliceId // намагаємося підкинути айді аліси
        };
        using var response = await client.PostAsJsonAsync("/api/incidents", requestBody);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var incidentId= doc.RootElement.GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var incident = await db.Incidents.FindAsync(incidentId);
        Assert.NotNull(incident);
        Assert.Equal(DbSeeder.BobId, incident.OwnerUserId); // власник все одно боб
    }

    // А-06: після виходу повторний запит дає 401
    [Fact]
    public async Task A06_Logout_Returns401OnNextProtectedRequest()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword);
        using var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        using var meResponse = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    // A-07: невідомий користувач і хибний пароль дають однаковий зовнішній результат 
    [Fact]
    public async Task A07_UnknowUserAndWrongPassword_ReturnSameExternalResult()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var wrongUserResponse = await anonymous.PostAsJsonAsync("/api/auth/login", new { userName = "unknown_user", password = "Synthetic-Unknown-1!" });
        using var wrongPassResponse = await anonymous.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = "WrongPassword1!" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongUserResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassResponse.StatusCode);
        var jsonUser = await wrongUserResponse.Content.ReadAsStringAsync();
        var jsonPass = await wrongPassResponse.Content.ReadAsStringAsync();
        // traceId різний для кожного запиту, тому прибираємо його перед порівнянням
        Assert.Equal(StripTraceId(jsonUser), StripTraceId(jsonPass));
        Assert.Equal(wrongUserResponse.Content.Headers.ContentType?.MediaType, wrongPassResponse.Content.Headers.ContentType?.MediaType);
    }

    // асінк
    [Fact]
    public async Task LoginFailure_DoesNotReflectInput_AndSetsNoCookie()
    {
        const string ghostPassword = "Synthetic-Reflection-77!";
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = false });
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "ghost-d18", password = ghostPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // статус ще не доказ
        var body = await response.Content.ReadAsStringAsync();          // лише в пам'ять, не друкуємо
        var leaked = body.Contains(ghostPassword, StringComparison.Ordinal);
        var names = string.IsNullOrWhiteSpace(body) ? new HashSet<string>() : JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        var allowed = new HashSet<string> { "type", "title", "status", "traceId" };
        Assert.True(!leaked && names.SetEquals(allowed), "Login failure body is not safe");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
    private static string StripTraceId(string json) => Regex.Replace(json, "\"traceId\"\\s*:\\s*\"[^\"]*\"", "");
}
