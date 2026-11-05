using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using System.Security.Claims;
using System.Threading.RateLimiting;
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
        // обмеження повторних спроб входу: ключем є ІР, даємо 10 спроб на 10 секунд
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(10),
                        QueueLimit = 0
                    }));
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        // обов'язково вмикаємо middleware для rate limiter на початку 
        app.UseRateLimiter();
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
        app.MapPost("/api/auth/login", async (LoginInput input, SignInManager<ApplicationUser> signIn, HttpContext httpContext, ILogger<LoginInput> logger) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
            {
                // логуємо провал через пусті поля 
                logger.LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}",
                    "login", "rejected-empty", httpContext.TraceIdentifier);
                return Results.Unauthorized();
            }
            var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
            // пишемо подію в журнал (success або failure), юзернейм навмисно не логуємо, бо можливо він випадково був введений у поле логіна 
            // тому логування імені може призвести до зливу паролів у відкритому вигляді
            logger.LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}", "login", result.Succeeded ? "success" : "failure", httpContext.TraceIdentifier);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("login"); // прив'язуєм політику лімітів
        // замінюємо заглушку 501 на Вихід 
        app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
        // в /api/me прибираємо X-Demo-UserId, беремо ID з кукі та додаємо RequireAuthorization()
        app.MapGet("/api/me", async (HttpContext context, SecureLabDbContext db) =>
        {
            var userIdString = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdString, out var claimedId))
                return Results.Unauthorized();
            var user = await db.Users.FindAsync(claimedId);
            return user is null ? Results.Unauthorized() : Results.Ok(new { user.Id, user.UserName, user.DisplayName });
        }).RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);

public sealed record RegisterResponse(Guid Id, string UserName, string DisplayName);