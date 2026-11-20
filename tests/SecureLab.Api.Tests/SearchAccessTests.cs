using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

// перевірка фіксу F-01: пошук тепер не віддає дані анонімам і фільтрує чужі інциденти за роллю
public sealed class SearchAccessTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static async Task<(Guid Id, string Title)> CreateAsync(HttpClient client)
    {
        var title = $"F01 {Guid.NewGuid():N}";
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "опис для тесту пошуку",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
        return (created!.Id, title);
    }

    private static async Task<List<IncidentSearchResponse>> SearchAsync(HttpClient client, string q)
    {
        using var response = await client.GetAsync($"/api/incidents/search?q={Uri.EscapeDataString(q)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<IncidentSearchResponse>>())!;
    }

    [Fact]
    public async Task Search_Anonymous_Returns401_AndNoIncidentData()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await anonymous.GetAsync("/api/incidents/search?q=%25");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("description", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_AsReporter_FindsOwnIncident_ButNotForeignOne()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword);
        using var bob = await factory.LoginAsync("bob", SeedPassword);
        var (id, title) = await CreateAsync(alice);
        try
        {
            var own = await SearchAsync(alice, title);
            Assert.Contains(own, item => item.Id == id);            // власний інцидент має знаходитись

            var foreign = await SearchAsync(bob, title);
            Assert.DoesNotContain(foreign, item => item.Id == id);  // чужий інцидент у видачі bob не з'являється
            Assert.Empty(foreign);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Search_AsAnalyst_FindsForeignIncident()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword);
        using var morgan = await factory.LoginAsync("morgan", SeedPassword);
        var (id, title) = await CreateAsync(alice);
        try
        {
            var found = await SearchAsync(morgan, title);
            Assert.Contains(found, item => item.Id == id);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Search_AsAdministrator_Returns403()
    {
        using var admin = await factory.LoginAsync("admin", SeedPassword);
        using var response = await admin.GetAsync("/api/incidents/search?q=a");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
