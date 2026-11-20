using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentChildResourceTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid PublicCommentOnAlice = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CommentOnBob = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid InternalCommentOnAlice = Guid.Parse("30000000-0000-0000-0000-000000000003");

    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private Task<HttpClient> LoginAsync(string userName) =>
        factory.LoginAsync(userName, SeedPassword);

    [Fact]
    public async Task GetComment_Anonymous_Returns401()
    {
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await anonymous.GetAsync($"/api/incident-comments/{PublicCommentOnAlice}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetComment_AsOwner_ReadsOwnPublicComment()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync($"/api/incident-comments/{PublicCommentOnAlice}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetComment_AsReporter_ForeignComment_Returns403_AndDoesNotLeakText()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync($"/api/incident-comments/{CommentOnBob}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Розпочато перевірку", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetComment_AsOwner_InternalComment_Returns403_AndDoesNotLeakText()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync($"/api/incident-comments/{InternalCommentOnAlice}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("цільовий фішинг", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetComment_UnknownId_Returns404()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync($"/api/incident-comments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetComment_AsAnalyst_CanReadInternalComment()
    {
        using var morgan = await LoginAsync("morgan");

        using var response = await morgan.GetAsync($"/api/incident-comments/{InternalCommentOnAlice}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var comment = await response.Content.ReadFromJsonAsync<IncidentCommentResponse>();
        Assert.Equal("Морган Литвин", comment!.AuthorDisplayName);
    }

    [Fact]
    public async Task Details_AsOwner_DoesNotContainInternalComment()
    {
        using var alice = await LoginAsync("alice");

        var details = await alice.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.DoesNotContain(details!.Comments, c => c.Id == InternalCommentOnAlice);
        Assert.Contains(details.Comments, c => c.Id == PublicCommentOnAlice);
    }

    [Fact]
    public async Task Details_AsAnalyst_ContainsInternalComment()
    {
        using var morgan = await LoginAsync("morgan");

        var details = await morgan.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Contains(details!.Comments, c => c.Id == InternalCommentOnAlice);
    }
}