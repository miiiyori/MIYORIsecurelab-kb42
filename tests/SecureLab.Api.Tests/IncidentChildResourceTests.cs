using System.Net;
using System.Net.Http.Json;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentChildResourceTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task GetComment_AsReporter_CannotReadForeignOrInternalComment()
    {
        using var alice = await factory.LoginAsync("alice", SeedPassword);

        // 1. аліса намагається прочитати комент моргана до інциденту боба (comment-2)
        var bobCommentId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        using var responseForeign = await alice.GetAsync($"/api/incident-comments/{bobCommentId}");
        Assert.Equal(HttpStatusCode.NotFound, responseForeign.StatusCode);

        // 2. аліса намагається прочитати внутрішній коментар до СВОГО інциденту (comment-3)
        var internalCommentId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        using var responseInternal = await alice.GetAsync($"/api/incident-comments/{internalCommentId}");
        Assert.Equal(HttpStatusCode.NotFound, responseInternal.StatusCode);
    }

    [Fact]
    public async Task GetComment_AsAnalyst_CanReadInternalComment()
    {
        using var morgan = await factory.LoginAsync("morgan", SeedPassword);

        var internalCommentId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        using var response = await morgan.GetAsync($"/api/incident-comments/{internalCommentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var comment = await response.Content.ReadFromJsonAsync<IncidentCommentResponse>();
        Assert.NotNull(comment);
        Assert.Equal("Морган Литвин", comment.AuthorDisplayName);
    }
}