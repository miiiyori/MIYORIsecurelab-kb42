# Матриця доступу ЛР04

Рівень: добрий. Baseline 2-A, ресурс Incident, дочірній ресурс IncidentComment.

## Політика відмов

- Анонім до захищеного endpoint: 401.
- Автентифікований користувач без права на існуючий ресурс: 403 (details, update, delete, triage, child).
- Неіснуючий id: 404.
- Обґрунтування: існування інциденту не секретне, а явна відмова простіша для клієнта й тестів. Тому A-10 не застосовується.
- Administrator прав на Incident не має, бо предметне правило їх прямо не називає. Виняток: створення інциденту лишилось успадкованим baseline.

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Список GET /api/incidents | добрий | deny, 401 | allow, лише власні | allow, усі | deny, 403 | так, для Reporter | IncidentQueries.GetListAsync; тести GetList_AsReporter, GetList_AsAnalyst |
| Деталі GET /api/incidents/{id} | достатній | deny, 401 | owner allow; чужий deny, 403 | allow, будь-який інцидент | deny, 403 | так | IncidentAuthorizationHandler (Read); тести GetDetails_* |
| Створення POST /api/incidents | baseline | deny, 401 | allow, owner з principal | allow, owner з principal | allow, owner з principal | сервер призначає власника | успадковано з ЛР 3 |
| Update PUT /api/incidents/{id} | добрий | deny, 401 | owner allow (title, description, severity, occurredAtUtc); чужий deny, 403 | deny, 403 | deny, 403 | так | handler (Update); тести Update_* |
| Delete DELETE /api/incidents/{id} | добрий | deny, 401 | owner allow; чужий deny, 403 | deny, 403 | deny, 403 | так | handler (Delete); тести Delete_* |
| Triage POST /api/incidents/{id}/triage | добрий | deny, 401 | deny, 403, навіть для власника | allow, лише зі статусу New, інакше 409; новий статус ставить сервер | deny, 403 | ні | handler (Triage); тести Triage_*, audit authorization.denied |
| Зміна status/owner/role через update | добрий | deny, 401 | не приймається, DTO цих полів не містить | deny, 403 | deny, 403 | так | UpdateIncidentRequest; тест Update_WithForgedFields |
| Child GET /api/incident-comments/{id} | добрий | deny, 401 | allow, якщо parent свій і коментар не internal; інакше deny, 403 | allow, включно з internal | deny, 403 | доступ до parent перевіряється окремо | GetCommentAsync; тести GetComment_* |
| Internal comment у details | добрий | deny, 401 | не показується | показується | deny, 403 | ні | includeInternal у GetDetailsAsync; тести Details_* |
| Search, severity-summary | відмінний | не виконувалось: рівень відмінний | не виконувалось | не виконувалось | не виконувалось | не виконувалось | не виконувалось |
| Export/audit | відмінний | не застосовується | не застосовується | не застосовується | не застосовується | не застосовується | не застосовується |