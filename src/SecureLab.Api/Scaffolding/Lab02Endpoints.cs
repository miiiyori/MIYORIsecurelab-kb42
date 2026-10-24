using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;


namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (
            string? q,
            string? sortBy,
            SecureLabDbContext db,
            CancellationToken ct) =>
        {
            // збираємо патерн для пошуку. якщо q пусте, шукаємо просто "%%", тобто все підряд
            var pattern = "%" + EscapeLike(q ?? "") + "%";
            var found = db.Incidents.AsNoTracking().Where(item => EF.Functions.ILike(item.Title, pattern, "\\")
            || EF.Functions.ILike(item.Description, pattern, "\\"));
            
            // sortBy обирає лише одну з написаних гілок
            // для статусів і рівнів прописуємо ранги вручну бо звичайний алфавітний порядок нам не підходить 
            IQueryable<Incident>? ordered = sortBy switch
            {
                null or "" or "createdAtUtc" => found
                    .OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id),
                "severity" => found
                .OrderBy(item => item.Severity == IncidentSeverity.Critical ? 0
                    : item.Severity == IncidentSeverity.High ? 1
                    : item.Severity == IncidentSeverity.Medium ? 2 : 3)
                .ThenBy(item => item.Id),
                "status" => found
                    .OrderBy(item => item.Status == IncidentStatus.New ? 0
                    : item.Status == IncidentStatus.Triaged ? 1
                    : item.Status == IncidentStatus.InProgress ? 2
                    : item.Status == IncidentStatus.Resolved ? 3 : 4)
                .ThenBy(item => item.Id),
                // якщо прислали якийсь брєд замість sortBy
                _ => null
            };
            // сортування не з нашого списку - помилка 400
            if (ordered is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status"]
                });
            var items = await ordered
            .Take(50)
            .Select(item => new IncidentSearchResponse(
                item.Id, item.Title, item.Description, item.Severity.ToString(), item.Status.ToString(), item.CreatedAtUtc))
            .ToListAsync(ct);
            return Results.Ok(items);
        });

        app.MapPost("/api/incidents", async (
            CreateIncidentRequest request,
            SecureLabDbContext db,
            CancellationToken ct) =>
        {
            // ЛР 02: доповніть server validation, business rules та response contract.
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            // перевірки
            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Назва обов'язкова"];
            else if (request.Title.Length > 160)
                errors["title"] = ["Назва не може перевищувати 160 символів"];

            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис обов'язковий"];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Опис не може перевищувати 4000 символів"];

            // severity
            var severityIsValid = Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
            && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical"];

            // дата обов'язкова і не більше за +5хв
            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["Дата події обов'язкова"];
            else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Дата не може бути більш ніж на 5 хвилин у майбутньому"];

            // нормалізація один раз
            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            // T-09: опис для High/Critical від 40 символів
            if (severityIsValid
            && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
            && !errors.ContainsKey("description")
            && description.Length < 40)
                errors["description"] = ["Для високого та критичного рівня опис має містити щонайменше 40 символів"];

            // усі помилки контракту - відповідь 400, до БД
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // T-03: предметний конфлікт, повторний тайтл
            var duplicateExits = await db.Incidents.AnyAsync(
                item => item.Title == title && item.Status != IncidentStatus.Closed, ct);
            if (duplicateExits)
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Інцидент уже існує",
                    detail: "Активний інцидент з такою назвою уже створено");

            // entity, лише сервер задає службові поля
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            // response DTO
            var response = new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Severity.ToString(),
                incident.Status.ToString(), incident.OccurredAtUtc, incident.CreatedAtUtc);
            return Results.Created($"/api/incidents/{incident.Id}", response);
        });
    }
    // екрануємо спецсимволи postgresql слеш/підкреслення/відсоток, щоб вони шукалися як звичайні букви 
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("_", "\\_").Replace("%", "\\%");
}
public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);
// response record
public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status, DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);
public sealed record IncidentSearchResponse(
    Guid Id, string Title, string Description, string Severity, string Status, DateTimeOffset CreatedAtUtc);
