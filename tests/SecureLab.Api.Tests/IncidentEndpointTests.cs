using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    // кожен користувач логіниться окремим клієнтом, щоб cookie не змішувались
    private Task<HttpClient> LoginAsync(string userName) =>
        factory.LoginAsync(userName, SeedPassword);

    [Fact]
    public async Task GetList_ReturnsSeededIncidents()
    {
        // список поки не обмежений, тест перепишу, коли дійдемо до scoping
        using var alice = await LoginAsync("alice");

        var incidents = await alice.GetFromJsonAsync<List<IncidentListItemResponse>>(
            "/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
    }

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.Equal("Аліса Коваль", document.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }
    // A-01
    [Fact]
    public async Task GetDetails_Anonymous_Returns401_NotRedirect()
    {
        // без редіректів, щоб побачити справжню відповідь api
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await anonymous.GetAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }
    // A-02
    [Fact]
    public async Task GetDetails_AsOwner_ReturnsOwnIncident()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await response.Content.ReadFromJsonAsync<IncidentDetailsResponse>();
        Assert.Equal(DbSeeder.AliceIncidentId, details!.Id);
    }
    // A-03
    [Fact]
    public async Task GetDetails_AsNonOwner_Returns403_AndDoesNotLeakData()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}");
        var body = await response.Content.ReadAsStringAsync();

        // крім статусу перевіряю, що дані боба не просочились у тіло
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Невідома спроба входу", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Боб Мельник", body, StringComparison.Ordinal);
    }
    // A-03R
    [Fact]
    public async Task GetDetails_AsAnalyst_ReturnsForeignIncident()
    {
        using var morgan = await LoginAsync("morgan");

        using var response = await morgan.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await response.Content.ReadFromJsonAsync<IncidentDetailsResponse>();
        Assert.Equal(DbSeeder.AliceIncidentId, details!.Id);
    }
}