using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Security;

namespace SecureLab.Api.Tests;

public static class CookieClient
{
    private sealed record TokenResponse(string Token);

    // вхід тільки з cookie, без отримання csrf-токена - потрібен, щоб перевірити відмову на зміну даних без токена
    public static async Task<HttpClient> LoginWithoutTokenAsync(
        this WebApplicationFactory<Program> factory, string userName, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException("Study login failed (credentials omitted).");
        }
        return client;
    }

    // імітує звичайний клієнт: тримає cookie-сеанс і автоматично додає csrf-токен у заголовок
    public static async Task<HttpClient> LoginAsync(
        this WebApplicationFactory<Program> factory, string userName, string password)
    {
        var client = await factory.LoginWithoutTokenAsync(userName, password);
        try
        {
            await client.UseAntiforgeryTokenAsync();
        }
        catch
        {
            client.Dispose();
            throw;
        }
        return client;
    }

    // запит токена йде через той самий HttpClient, щоб разом із токеном зберігся antiforgery cookie
    public static async Task<string> GetCsrfTokenAsync(this HttpClient client)
    {
        using var response = await client.GetAsync(CsrfDefaults.TokenRoute);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrWhiteSpace(body?.Token))
            throw new InvalidOperationException("Missing request token.");
        return body.Token;
    }

    public static async Task UseAntiforgeryTokenAsync(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove(CsrfDefaults.HeaderName);
        client.DefaultRequestHeaders.Add(CsrfDefaults.HeaderName, await client.GetCsrfTokenAsync());
    }
}
