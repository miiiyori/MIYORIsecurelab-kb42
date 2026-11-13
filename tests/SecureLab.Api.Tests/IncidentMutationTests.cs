using System.Net;
using System.Net.Http.Json;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class IncidentMutationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private Task<HttpClient> LoginAsync(string userName) =>
        factory.LoginAsync(userName, SeedPassword);

    // створюю власний інцидент, щоб тест не залежав від seed і запускався повторно
    private static async Task<Guid> CreateIncidentAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = $"Тест {Guid.NewGuid():N}",
            description = "опис для тесту",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
        return created!.Id;
    }

    private static async Task<IncidentDetailsResponse> ReadAsync(HttpClient client, Guid id)
    {
        var details = await client.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}");
        return details!;
    }

    private static object ValidUpdate(string title) => new
    {
        title,
        description = "оновлений опис",
        severity = "Medium",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    [Fact]
    public async Task Update_AsOwner_ChangesContent()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateIncidentAsync(alice);
        try
        {
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", ValidUpdate("нова назва"));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await ReadAsync(alice, id);
            Assert.Equal("нова назва", after.Title);
            Assert.Equal("Medium", after.Severity);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Update_AsNonOwner_Returns403_AndDoesNotChangeIncident()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        var id = await CreateIncidentAsync(alice);
        try
        {
            var before = await ReadAsync(alice, id);

            using var response = await bob.PutAsJsonAsync($"/api/incidents/{id}", ValidUpdate("зламано бобом"));

            // крім статусу дивлюсь, що дані справді не змінились
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var after = await ReadAsync(alice, id);
            Assert.Equal(before.Title, after.Title);
            Assert.Equal(before.Severity, after.Severity);
            Assert.Equal(before.Description, after.Description);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Update_AsAnalyst_Returns403_AndDoesNotChangeIncident()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");
        var id = await CreateIncidentAsync(alice);
        try
        {
            var before = await ReadAsync(alice, id);

            using var response = await morgan.PutAsJsonAsync($"/api/incidents/{id}", ValidUpdate("зламано морганом"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before.Title, (await ReadAsync(alice, id)).Title);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Update_Anonymous_Returns401()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.PutAsJsonAsync(
            $"/api/incidents/{Guid.NewGuid()}", ValidUpdate("x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithForgedFields_IgnoresOwnerStatusAndRole()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateIncidentAsync(alice);
        try
        {
            var before = await ReadAsync(alice, id);

            // зайві поля мають мовчки ігноруватись
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", new
            {
                title = "зі зайвими полями",
                description = "оновлений опис",
                severity = "Medium",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                ownerUserId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                status = "Closed",
                role = "Analyst"
            });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            // alice досі читає інцидент як власник, статус і власник не змінились
            var after = await ReadAsync(alice, id);
            Assert.Equal("зі зайвими полями", after.Title);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.OwnerDisplayName, after.OwnerDisplayName);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Delete_AsNonOwner_Returns403_AndKeepsIncident()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        var id = await CreateIncidentAsync(alice);
        try
        {
            using var response = await bob.DeleteAsync($"/api/incidents/{id}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var stillThere = await alice.GetAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        }
        finally
        {
            await alice.DeleteAsync($"/api/incidents/{id}");
        }
    }

    [Fact]
    public async Task Delete_AsOwner_Returns204_AndIncidentIsGone()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateIncidentAsync(alice);

        using var response = await alice.DeleteAsync($"/api/incidents/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var afterDelete = await alice.GetAsync($"/api/incidents/{id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }
}