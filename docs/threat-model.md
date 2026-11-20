# Модель загроз SecureLab (ЛР 6)

Рівень виконання: **добрий ("Добре")**. Базовий варіант 2-A, основний ресурс - `Incident`, дочірній - `IncidentComment`.

| Елемент | Опис | Деталі |
|---|---|---|
| Версія та середовище | Початок огляду на гілці `lab/6-hardening-review` від тегу `v0.5.0`. Основна робота в `Development`, перевірка безпечної конфігурації та 500-помилок - у `Production`. Скидання БД через `dotnet run --project src/SecureLab.Api -- --reset-database`. Використовуються лише тестові акаунти `alice`, `bob`, `morgan`, `admin` | review-start: `09667b6`; SDK: `10.0.403` |
| Компоненти в межах огляду | Браузерний клієнт (`src/SecureLab.Api/Client`), ASP.NET Core API, PostgreSQL у Docker Compose (`infra/compose.yaml`, `127.0.0.1:54329`), файли конфігурації (`appsettings*.json`, змінні середовища), консольні логи. Redis не використовується | |
| Активи | Облікові дані (`PasswordHash` в Identity); інциденти користувачів (`title`, `description`); привілейований перехід статусу `Incident.Status` (triage); audit-записи (`IncidentStatusHistory`, події в логах); параметри конфігурації (connection string, `SeedUsers__Password`) | |
| Суб’єкти | Анонімний користувач; `Reporter` (`alice`, `bob`); `Analyst` (`morgan`); `Administrator` (`admin`, не має доступу до `Incident`); локальний розробник | див. `docs/access-matrix.md` |
| Точки входу (Entry points) | `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/me`, `GET /api/auth/csrf-token`, `GET /api/incidents`, `GET /api/incidents/{id}`, `GET /api/incidents/search`, `GET /api/incidents/severity-summary`, `POST /api/incidents`, `PUT`/`DELETE /api/incidents/{id}`, `POST /api/incidents/{id}/triage`, `GET /api/incident-comments/{id}`, `GET /health`; конфігурація при старті; `dotnet restore`; логи | змінювальні маршрути — розділ 8 `security-review.md` |
| Межі довіри | browser → API; API -> PostgreSQL; repository -> build; environment -> configuration | схема нижче |
| Наявні механізми захисту | Серверна валідація (ЛР 2), параметризовані запити через EF Core (ЛР 2), Identity + cookie-сеанс (ЛР 3), rate limit на login (ЛР 3), ресурсна авторизація (ЛР 4), безпечний вивід у DOM через `textContent` і CSP (ЛР 5), CORS лише в Development (ЛР 5), **antiforgery (ЛР 6)**, **rate limit на пошук (ЛР 6)**, **заголовки безпеки на 500-відповідях (ЛР 6)**, **audit успішного triage (ЛР 6)** | перевірки наведено в таблиці нижче |
| Поза межами огляду | Зовнішній pentest; реальний production-сервер із TLS/HTTPS і HSTS (стенд працює по локальному HTTP); перевірка всієї історії Git, stash, інших клонів чи зовнішнього CI; аналіз невідомих вразливостей у залежностях; Redis і розподілений rate limit; навантажувальне тестування та DoS | |

## Схема меж довіри
```
browser client (недовірений ввід, автоматична передача auth cookie)
│  локальний HTTP (Development) / Secure cookie (Production)
▼
ASP.NET Core API
[ headers → exception handler → routing → CORS(Dev) → authn → authz → rate limiter → endpoint filter (antiforgery) → handler ]
│  connection string / EF Core (параметризовані запити)
▼
PostgreSQL (дані застосунку, identity_*, incident_status_history)

repository + NuGet ──► restore/build (packages.lock.json)
environment config ──► запуск API (ConnectionStrings__SecureLab, SeedUsers__Password)
```

## Очікувані властивості на межах довіри

| Межа | Очікувана поведінка | Тест або перевірка |
|---|---|---|
| browser -> API | Змінювальний запит із cookie без валідного CSRF-токена відхиляється (`400`) без зміни даних; із валідною парою проходить | **REG-CSRF-01**: `CsrfRegressionTests.RegCsrf01_PutWithoutToken_IsRejected_StateUnchanged_ThenValidPairSucceeds`, `AllCookieMutations_WithoutOrWithInvalidToken_AreRejected_AndStateIsUnchanged` |
| browser -> API | Текст від користувача виводиться як звичайний текст і не виконується як код | **REG-XSS-01** (перевірка ЛР 5) + `IncidentEndpointTests.ClientScript_DoesNotUseDangerousInnerHtmlSink` |
| browser -> API | Чужий origin не отримує CORS-дозволу; поза Development CORS-політика вимкнена | **REG-CORS-01**: `CorsRegressionTests.*` + браузерна перевірка ЛР 5 |
| browser -> API | Пошук і список інцидентів обмежені власником і роллю; анонім не отримує даних | `SearchAccessTests.*` (retest F-01), `IncidentEndpointTests.GetList_*` (A-05) |
| browser -> API | Зловживання входом і пошуком обмежується, лічильники різних користувачів ізольовані | `SearchRateLimitTests.*`, `LoginRateLimitTests.*` |
| browser -> API | Усі класи відповідей (включно з `500`) мають базові заголовки безпеки; `500` не розкриває внутрішніх деталей | **REG-HDR-01**: `SecurityHeadersTests.*` (retest F-02) |
| API -> PostgreSQL | Рядок пошуку не впливає на структуру SQL-запиту | **S-02** / **T-04** (ЛР 2): `SearchMechanicsTests.Search_AsAuthenticatedUser_ReturnsJson` і перевірки з апострофом |
| API -> PostgreSQL | Сервер ігнорує підкинуті в JSON поля `ownerUserId`, `status`, `role` | A-05 (ЛР 3), A-08a (ЛР 4): `Update_WithForgedFields_IgnoresOwnerStatusAndRole` |
| repository -> build | У tracked-файлах немає реальних секретів; навчальний пароль БД явно обмежений локальним стендом; restore відтворюваний | `git grep` (розділ 9 `security-review.md`), `dotnet restore --locked-mode`, `dotnet package list --vulnerable --include-transitive` |
| environment -> configuration | У `Production` вимкнені dev-механізми (`--reset-database`, OpenAPI, CORS, автоміграції) | Лог `Hosting environment: Production` + відмова `--reset-database` (`lr6-prod-env.png`); `CorsRegressionTests.Production_*` |
| API -> журнал | Логи не містять паролів, cookie й токенів, але фіксують audit-події | `LogHygieneTests.Logs_DoNotContainCredentialsCookiesOrTokens_AndAuditEventsAreWritten` + перевірка логів через `Select-String` (`lr6-log-check.png`) |