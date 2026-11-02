using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
        {
            // усі поля обов'язкові, пароль лише перевіряємо, щоб не був порожній 
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrWhiteSpace(input.Email)
                || string.IsNullOrWhiteSpace(input.DisplayName) || string.IsNullOrEmpty(input.Password))
                return Results.BadRequest(new { message = "Всі поля обов'язкові!" });
            // обрізаємо пробіли один раз і далі працюємо тільки з цими змінними
            var userName = input.UserName.Trim();
            var email = input.Email.Trim();
            var displayName = input.DisplayName.Trim();
            // перевірка на довжину 
            if (displayName.Length > 120 || userName.Length > 256 || email.Length > 256)
                return Results.BadRequest(new { message = "Перевищено допустиму довжину полів" });
            // Id, роль і хеш пароля ставить сервер, тому їх немає у RegistrationInput
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = userName, Email = email,DisplayName = displayName
            };
            // пароль хешує сам Identity всередині CreateAsync 
            var result = await users.CreateAsync(user, input.Password);
            if (!result.Succeeded)
            {
                var codes = result.Errors.Select(error => error.Code).ToArray();
                // duplicate імені чи імейлу => 409 Conflict 
                if (codes.Contains("DuplicateUserName") || codes.Contains("DuplicateEmail"))
                    return Results.Conflict(new { message = "Користувач з таким іменем або email уже існує." });
                // інші помилки (типу слабкий пароль, невалідне ім'я чи імейл повертають 400, лише коди Identity, без пароля і самого запиту)
                return Results.BadRequest(new { message = "Помилка реєстрації", errors = codes });
            }
            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            // у відповіді лише безпечні поля 
            var response = new RegisterResponse(user.Id, user.UserName, user.DisplayName);
            return Results.Created("/api/me", response);
        });
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
                return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        app.MapPost("/api/auth/logout", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        // Навмисно хибна навчальна довіра: ЛР 03 замінює заявлений id на verified principal.
        app.MapGet("/api/me", async (HttpContext context, SecureLabDbContext db) =>
        {
            if (!Guid.TryParse(context.Request.Headers["X-Demo-UserId"], out var claimedId))
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            var user = await db.Users.FindAsync(claimedId);
            return user is null ? Results.NotFound() : Results.Ok(new { user.Id, user.UserName, user.DisplayName });
        });
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);

public sealed record RegisterResponse(Guid Id, string UserName, string DisplayName);