using Microsoft.AspNetCore.Authorization;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

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

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
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

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
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
}