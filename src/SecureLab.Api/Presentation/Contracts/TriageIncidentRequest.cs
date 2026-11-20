namespace SecureLab.Api.Presentation.Contracts;

// клієнт передає лише текст нотатки, статус визначає сервер
public sealed record TriageIncidentRequest(string? Note);