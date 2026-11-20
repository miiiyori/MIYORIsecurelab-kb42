using System.Net;
using System.Net.Http.Json;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;
using SecureLab.Api.Security;

namespace SecureLab.Api.Tests;

// REG-CSRF-01 (ЛР 6): cookie-authenticated змінювальний запит без валідного request token відхиляється
// БЕЗ зміни стану; той самий запит із валідною парою токенів виконується
public sealed class CsrfRegressionTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static HttpRequestMessage WithToken(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add(CsrfDefaults.HeaderName, token);
        return request;
    }

    private static object ValidUpdate(string title) => new
    {
        title,
        description = "оновлений опис",
        severity = "Medium",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    // повний сценарій: логін -> запит без токена (400) -> перевірка, що запис не змінився -> той самий запит із токеном проходить
    [Fact]
    public async Task RegCsrf01_PutWithoutToken_IsRejected_StateUnchanged_ThenValidPairSucceeds()
    {
        using var client = await factory.LoginWithoutTokenAsync("alice", SeedPassword);   // cookie є, токена немає
        var token = await client.GetCsrfTokenAsync();

        // підготовка ресурсу (створення теж змінювальне, тому з токеном)
        var title = $"CSRF {Guid.NewGuid():N}";
        using var createRequest = WithToken(HttpMethod.Post, "/api/incidents", token, new
        {
            title,
            description = "опис для CSRF-тесту",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        using var created = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedIncidentResponse>())!.Id;

        try
        {
            var before = (await client.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}"))!;

            // негативна частина: auth-cookie є, request token немає
            using var rejected = await client.PutAsJsonAsync($"/api/incidents/{id}", ValidUpdate("зламано без токена"));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var afterRejected = (await client.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}"))!;
            Assert.Equal(before.Title, afterRejected.Title);                 // стан не змінився
            Assert.Equal(before.Description, afterRejected.Description);
            Assert.Equal(before.Severity, afterRejected.Severity);

            // позитивна частина: валідна пара (cookie + request token)
            using var accepted = await client.SendAsync(
                WithToken(HttpMethod.Put, $"/api/incidents/{id}", token, ValidUpdate("змінено з токеном")));
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            var afterAccepted = (await client.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}"))!;
            Assert.Equal("змінено з токеном", afterAccepted.Title);         // саме очікувана зміна
        }
        finally
        {
            using var cleanup = await client.SendAsync(WithToken(HttpMethod.Delete, $"/api/incidents/{id}", token));
        }
    }

    // проходимо всі змінювальні маршрути, щоб переконатись, що жоден не пропускає запит без токена або з битим токеном
    [Fact]
    public async Task AllCookieMutations_WithoutOrWithInvalidToken_AreRejected_AndStateIsUnchanged()
    {
        using var client = await factory.LoginWithoutTokenAsync("alice", SeedPassword);
        var token = await client.GetCsrfTokenAsync();
        var title = $"CSRF perimeter {Guid.NewGuid():N}";
        using var createRequest = WithToken(HttpMethod.Post, "/api/incidents", token, new
        {
            title,
            description = "опис для перевірки периметра",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        using var created = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedIncidentResponse>())!.Id;

        try
        {
            foreach (var invalidToken in new string?[] { null, "not-a-real-token" })
            {
                HttpRequestMessage Build(HttpMethod method, string url, object? body = null)
                {
                    var request = new HttpRequestMessage(method, url);
                    if (body is not null) request.Content = JsonContent.Create(body);
                    if (invalidToken is not null) request.Headers.Add(CsrfDefaults.HeaderName, invalidToken);
                    return request;
                }

                // PUT
                using (var put = await client.SendAsync(Build(HttpMethod.Put, $"/api/incidents/{id}", ValidUpdate("x"))))
                    Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
                // triage (проти Reporter відмова була б 403, але antiforgery спрацьовує раніше за бізнес-перевірку)
                using (var triage = await client.SendAsync(Build(HttpMethod.Post, $"/api/incidents/{id}/triage", new { note = "x" })))
                    Assert.Equal(HttpStatusCode.BadRequest, triage.StatusCode);
                // POST create
                using (var create = await client.SendAsync(Build(HttpMethod.Post, "/api/incidents", new
                {
                    title = $"CSRF should-not-exist {Guid.NewGuid():N}",
                    description = "не має створитися",
                    severity = "Low",
                    occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                })))
                    Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
                // DELETE
                using (var delete = await client.SendAsync(Build(HttpMethod.Delete, $"/api/incidents/{id}")))
                    Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
                // logout: токен перевіряється до SignOutAsync, сеанс лишається чинним
                using (var logout = await client.SendAsync(Build(HttpMethod.Post, "/api/auth/logout")))
                    Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
            }

            // стан: інцидент на місці, без змін, статус New, сеанс чинний
            var after = (await client.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}"))!;
            Assert.Equal(title, after.Title);
            Assert.Equal("New", after.Status);
            using var me = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            using var cleanup = await client.SendAsync(WithToken(HttpMethod.Delete, $"/api/incidents/{id}", token));
        }
    }

    // перевіряємо, що logout із валідним токеном нормально завершує сеанс і далі повертається 401
    [Fact]
    public async Task Logout_WithValidToken_Returns204_AndSessionEnds()
    {
        using var client = await factory.LoginAsync("alice", SeedPassword);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    // анонімний клієнт не має отримувати csrf-токен, він видається лише автентифікованому
    [Fact]
    public async Task TokenRoute_Anonymous_Returns401()
    {
        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false });
        using var response = await anonymous.GetAsync(CsrfDefaults.TokenRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
