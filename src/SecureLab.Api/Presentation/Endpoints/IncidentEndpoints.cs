using Microsoft.AspNetCore.Authorization;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();
        group.MapPut("/{id:guid}", UpdateAsync)
    .WithName("UpdateIncident")
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }
            parsedStatus = candidate;
        }
        var userIdValue = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var ownerId))
        {
            return Results.Challenge();
        }
        // адміністратор у матриці не має права на список
        if (httpContext.User.IsInRole(DbSeeder.AdministratorRole))
        {
            return Results.Forbid();
        }
        var seeAll = httpContext.User.IsInRole(DbSeeder.AnalystRole);
        return Results.Ok(await queries.GetListAsync(parsedStatus, ownerId, seeAll, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.FindAsync(id, cancellationToken);
        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var decision = await authorizationService.AuthorizeAsync(
            httpContext.User, incident, IncidentOperations.Read);
        if (!decision.Succeeded)
        {
            return Results.Forbid();
        }

        var includeInternal = httpContext.User.IsInRole(DbSeeder.AnalystRole);
        var details = await queries.GetDetailsAsync(id, includeInternal, cancellationToken);

        return details is null
            ? Results.Problem(title: "Інцидент не знайдено", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(details);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, 
        UpdateIncidentRequest request, 
        HttpContext httpContext, 
        IAuthorizationService authorizationService, 
        SecureLabDbContext db, 
        CancellationToken cancellationToken)
    {
        // без AsNoTracking, інакше SaveChanges нічого не запише
        var incident = await db.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // спочатку право, і лише потім валідація та зміна
        var decision = await authorizationService.AuthorizeAsync(
            httpContext.User, incident, IncidentOperations.Update);
        if (!decision.Succeeded)
        {
            return Results.Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var errors = new Dictionary<string, string[]>();

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";

        if (title.Length == 0)
            errors["title"] = ["Назва обов'язкова"];
        else if (title.Length > 160)
            errors["title"] = ["Назва не може перевищувати 160 символів"];

        if (description.Length == 0)
            errors["description"] = ["Опис обов'язковий"];
        else if (description.Length > 4000)
            errors["description"] = ["Опис не може перевищувати 4000 символів"];

        var severityIsValid = Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
            && Enum.IsDefined(severity);
        if (!severityIsValid)
            errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical"];

        if (request.OccurredAtUtc is null)
            errors["occurredAtUtc"] = ["Дата події обов'язкова"];
        else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
            errors["occurredAtUtc"] = ["Дата не може бути більш ніж на 5 хвилин у майбутньому"];

        if (severityIsValid
            && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
            && !errors.ContainsKey("description")
            && description.Length < 40)
            errors["description"] = ["Для високого та критичного рівня опис має містити щонайменше 40 символів"];

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // owner, status і id не чіпаємо, їх у запиті просто немає
        incident.Title = title;
        incident.Description = description;
        incident.Severity = severity;
        incident.OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime();
        incident.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        SecureLabDbContext db,
        CancellationToken cancellationToken)
    {
        var incident = await db.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var decision = await authorizationService.AuthorizeAsync(
            httpContext.User, incident, IncidentOperations.Delete);
        if (!decision.Succeeded)
        {
            return Results.Forbid();
        }

        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}