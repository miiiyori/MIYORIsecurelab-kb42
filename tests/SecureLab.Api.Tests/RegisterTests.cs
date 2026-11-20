using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecureLab.Api.Tests;
public sealed class RegisterTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
    // T-01: коректна реєстрація повертає 201 і правильний DTO
    [Fact]
    public async Task Register_ValidData_Returns201AndSafeResponse_T01()
    {
        // генеруємо унікальні дані, щоб тест завжди проходив 
        using var client = factory.CreateClient();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var request = new
        {
            userName = $"student_{uniqueSuffix}",
            email = $"student_{uniqueSuffix}@example.test",
            displayName = "Тестовий студент",
            password = StudyPassword
        };
        // Act
        using var response = await client.PostAsJsonAsync("/api/auth/register", request);
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        //перевіряємо, що повернулися тільки потрібні поля (Id, UserName, DisplayName)
        Assert.True(doc.RootElement.TryGetProperty("id", out _));
        Assert.Equal(request.userName, doc.RootElement.GetProperty("userName").GetString());
        Assert.Equal(request.displayName, doc.RootElement.GetProperty("displayName").GetString());

        // перевіряємо відсутність чутливих полів 
        Assert.False(doc.RootElement.TryGetProperty("password", out _));
        Assert.False(doc.RootElement.TryGetProperty("passwordHash", out _));
        Assert.False(doc.RootElement.TryGetProperty("role", out _));
    }
    // T-02: дублікат юзернейму або імейлу повертає 409 Conflict
    [Fact]
    public async Task Register_DuplicateUserOrEmail_Returns409Conflict_T02() 
    {
        // Arrange 
        using var client = factory.CreateClient();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var request = new
        {
            userName = $"dup_{uniqueSuffix}",
            email = $"dup_{uniqueSuffix}@example.test",
            displayName = "Дублікат",
            password = StudyPassword
        };
        // реєструємо першого користувача (має бути успішно)
        using var firstResponse = await client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        // Act - спроба зареєструвати того ж користувача вдруге 
        using var secondResponse = await client.PostAsJsonAsync("/api/auth/register", request);
        // Assert 
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var json = await secondResponse.Content.ReadAsStringAsync();
        // перевіряємо,що ми не зливаємо стек-трейс чи системні лог-повідомлення 
        Assert.DoesNotContain("Exception", json);
    }
}
