namespace SecureLab.Api.Presentation.Contracts;

//DTO для зміни статусу містить лише нотатку, статус визначає сервер
public sealed record TriageIncidentRequest(string Note);