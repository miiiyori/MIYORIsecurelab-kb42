using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;
public sealed class CreateIncidentTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    // Т-02: некоректний рівень северіті -> помилка 400 і правильний формат відповіді
    [Fact]
    public async Task Post_InvalidSeverity_Returns400_T02()
    {
        var title = $"Invalid-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "запис для перевірки некорекності северіті",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        try
        {
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync("/api/incidents", body);

            // перевіряємо статус і тип контенту
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            // чи є ключ северіті в помилках 
            Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("severity", out _));
            // перевіряємо чи не зливає сервер системні деталі
            Assert.DoesNotContain("Exception", json);
            Assert.DoesNotContain("   at ", json);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }
    // Т-03: дублікат назви -> конфлікт 409
    [Fact]
    public async Task Post_DublicateTitle_Returns409_T03()
    {
        var title = $"Dublicate-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "Дуже довгий опис щоб пройти перевірку на 40 символів для High. 123456789",
            severity = "High",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        try
        {
            using var client = factory.CreateClient();
            // перший запит має пройти успішно
            using var first = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            // другий такий самий має дати конфлікт 
            using var second = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

            var json = await second.Content.ReadAsStringAsync();
            // перевіряємо відсутність системних логів 
            Assert.DoesNotContain("Exception", json);
            Assert.DoesNotContain("   at ", json);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }
    // прибираємо лише власні записи тесту 
    private async Task DeleteByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }
}