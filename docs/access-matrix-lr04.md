# Матриця доступу ЛР04

Рівень виконання: добрий. Baseline 2-A, ресурс Incident, child IncidentComment.

## Політика відмов

- Анонім до будь-якого захищеного endpoint: 401.
- Автентифікований суб'єкт без права на існуючий ресурс: 403 (на всіх endpoint-ах: details, update, delete, triage, child).
- Неіснуючий id: 404.
- Обґрунтування: існування інциденту не є секретом для Reporter, а явна відмова простіша для клієнта і для тестів. Через це перевірка A-10 (нерозрізнюваність 404) не застосовується.
- Роль Administrator не має прав, прямо не опублікованих для Incident, тому скрізь deny. Роль Analyst читає, але не змінює content і не видаляє.

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник ресурсу має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Перегляд списку `GET /api/incidents` | добрий | deny, 401 | allow, лише власні інциденти | allow, усі інциденти | deny, 403 | Так для Reporter | (допишу: handler/query і тест A-05) |
| Пошук `GET /api/incidents/search` | відмінний (A-11) | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний |
| Зведення `GET /api/incidents/severity-summary` | відмінний (A-11) | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний | не виконувалось — рівень відмінний |
| Перегляд деталей `GET /api/incidents/{id}` | достатній | deny, 401 | owner — allow; non-owner — deny, 403 | allow для аналізу (чужі теж) | deny, 403 | Так | (A-02, A-03, A-03R) |
| Створення `POST /api/incidents` | успадкований baseline | deny, 401 | allow, owner береться з principal | allow, owner береться з principal | allow, owner береться з principal (окремого права немає, але й заборони в предметному правилі нема) | Сервер призначає власника | перевірити baseline |
| Update content `PUT /api/incidents/{id}` | добрий | deny, 401 | owner — allow (лише title, description, severity, occurredAtUtc); non-owner — deny, 403 | deny, 403 | deny, 403 | Так | (A-04, A-08a/b) |
| Delete `DELETE /api/incidents/{id}` | добрий | deny, 401 | owner — allow; non-owner — deny, 403 | deny, 403 | deny, 403 | Так | (A-04) |
| Triage `POST /api/incidents/{id}/triage` | добрий | deny, 401 | deny, 403 (і для власника) | allow, лише якщо поточний статус New; новий статус Triaged ставить сервер; інакше 409 | deny, 403 | Ні, рішення за роллю і статусом | (A-06, A-07, A-09) |
| Зміна severity/status через звичайний update | добрий | deny, 401 | deny: поля status/owner/role не приймаються DTO | deny, 403 | deny, 403 | Так | (A-08a/b) |
| Child `GET /api/incident-comments/{id}` | добрий | deny, 401 | allow, якщо parent належить йому і коментар не internal; інакше deny, 403 | allow, включно з internal | deny, 403 | Доступ до parent перевіряється окремо | (A-12) |
| Internal comment у details | добрий | deny, 401 | не показується | показується | deny, 403 | Ні для internal rule | (A-12) |
| Export/audit | відмінний | не застосовується | не застосовується | не застосовується | не застосовується | не застосовується | не застосовується |