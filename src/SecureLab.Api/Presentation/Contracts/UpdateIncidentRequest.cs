namespace SecureLab.Api.Presentation.Contracts;

// лише поля, які звичайний користувач може змінювати
public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity,
    DateTimeOffset? OccurredAtUtc);