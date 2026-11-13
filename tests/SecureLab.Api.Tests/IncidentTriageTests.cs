using System.Net;
using System.Net.Http.Json;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class IncidentTriageTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static async Task<Guid> CreateNewIncidentAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = $"Triage Test {Guid.NewGuid():N}",
            description = "опис для тесту",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
        return created!.Id;
    }

    [Fact]
    public async Task Triage_AsReporter_IsDenied_AndDoesNotChangeStatus()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword);
        var id = await CreateNewIncidentAsync(alice);

        try
        {
            var request = new { note = "спроба репортера" };
            using var response = await alice.PostAsJsonAsync($"/api/incidents/{id}/triage", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var after = await alice.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}");
            Assert.Equal("New", after!.Status); // доказ того, що статус не змінився
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Triage_AsAnalyst_ChangesStatusToTriaged()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword);
        using var morgan = await factory.LoginAsync("morgan", SeedPassword);

        var id = await CreateNewIncidentAsync(alice);
        try
        {
            var request = new { note = "переведено аналітиком" };
            using var response = await morgan.PostAsJsonAsync($"/api/incidents/{id}/triage", request);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var after = await alice.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}");
            Assert.Equal("Triaged", after!.Status); // доказ успішного переходу
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }
}