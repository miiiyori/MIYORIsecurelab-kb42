using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;

namespace SecureLab.Api.Security;

// назва заголовка і маршрут для видачі токена, ті самі значення юзає client/csrf.js
public static class CsrfDefaults
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string TokenRoute = "/api/auth/csrf-token";
}

// тут перевіряємо csrf-токен і перетворюємо помилку валідації на 400 Problem Details
public sealed class AntiforgeryValidationFilter(
    IAntiforgery antiforgery,
    ILogger<AntiforgeryValidationFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            // пишемо в лог тільки метод, маршрут і traceId. без токена, cookie й тіла запиту
            logger.LogWarning(
                "csrf.rejected method={Method} route={Route} traceId={TraceId}",
                http.Request.Method,
                (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown",
                http.TraceIdentifier);
            return Results.Problem(
                title: "Antiforgery token is missing or invalid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}

public static class AntiforgeryEndpointExtensions
{
    // вішаємо на всі змінювальні запити, які працюють через auth cookie
    public static RouteHandlerBuilder RequireAntiforgery(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<AntiforgeryValidationFilter>();
}
