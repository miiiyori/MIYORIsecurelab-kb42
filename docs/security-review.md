# Security review SecureLab (ЛР 6)

Рівень виконання: **добрий («Добре»)**. Пункти відмінного рівня позначено як такі, що не входять до обраного рівня.  
Початковий коміт огляду (review-start): `09667b6` (тег `v0.5.0`, гілка `lab/6-hardening-review`).  
Коміт із виправленнями коду: `3b08dd3`.  
Дата перевірки: `20.11.2026`, .NET SDK: `10.0.403`, профілі запуску: `Development` і `Production` (локально), клієнти: PowerShell (`curl.exe`), Google Chrome, xUnit-тести.

## 1. Короткий підсумок

Провела повний огляд стану `v0.5.0` застосунку SecureLab (трекер інцидентів, baseline 2-A): браузерний клієнт, ASP.NET Core API, PostgreSQL у Docker, конфігурацію та логи. Знайдено й виправлено три проблеми:
- **F-01 (P1)** - анонімний `GET /api/incidents/search` повертав усі інциденти в базі;
- **F-03 (P2)** - змінювальні маршрути з cookie-автентифікацією не перевіряли CSRF-токен;
- **F-02 (P3)** - на 500-відповідях зникали базові заголовки безпеки.

Додатково додано захисні механізми: перевірку antiforgery на всіх п’яти змінювальних cookie-маршрутах, rate limit на пошук із розділенням лічильників по користувачах та audit-запис успішного переходу `Incident.Status`.  
Фінальний прогін тестів (`dotnet test SecureLab.sln -c Release`, 20.11.2026): **PASS (63/63)**.  
Основні залишкові ризики: перевірка проводилася по локальному HTTP без реального HTTPS/HSTS, маршрути `login` і `register` не захищені CSRF-токеном, in-memory rate limit не синхронізується між кількома екземплярами API, а повна історія Git і невідомі вразливості залежностей не аналізувалися.

## 2. Межі огляду та виключення

**У межах:** усі поточні tracked-файли репозиторію (`git ls-files`), API, клієнтські сторінки, PostgreSQL у Docker Compose, файли конфігурації, консольні логи у профілях `Development` і `Production`.  
**Поза межами:** історія Git, stash, інші клони, зовнішній CI, реальний production-хост із TLS termination, Redis, навантажувальне тестування та невідомі вразливості сторонніх пакетів. Усі перевірки виконувалися на локальному навчальному стенді зі штучними акаунтами.

## 3. Огляд системи та межі довіри

Активи, суб’єкти, схема меж довіри (browser -> API, API -> PostgreSQL, repository -> build, environment -> configuration) і прив’язка кожної межі до конкретних тестів описані в `docs/threat-model.md`.

## 4. Середовище та методика перевірки

- **Стан:** гілка `lab/6-hardening-review` від `v0.5.0` (`09667b6`). Перед перевірками виконувалося скидання БД (`--reset-database`) у `Development`; PostgreSQL працював у Docker Compose на `127.0.0.1:54329`.
- **Клієнти:** `curl.exe -i` у PowerShell для ручних HTTP-перевірок (анонім, `alice`, `bob`, `morgan`, `admin`), Google Chrome (DevTools -> Мережа) для перевірки `auth-lab.html` та `browser-lab.html`.
- **Профілі:** `Development` (`dotnet run --project src/SecureLab.Api`) та `Production` (`--no-launch-profile`, `ASPNETCORE_ENVIRONMENT=Production`, рядок підключення через змінну середовища `ConnectionStrings__SecureLab`, `Logging__Console__IncludeScopes=true`).
- **Автоматизовані тести:** `dotnet test SecureLab.sln -c Release` (із заданою змінною `SeedUsers__Password` і запущеною БД). Результат отримано 20.11.2026 на SDK `10.0.403`: **63/63 passed**.

## 5. Зведена таблиця знайдених проблем (Findings)

| ID | Що під загрозою | Вплив | Ймовірність | Пріоритет | Статус | Перевірка |
|---|---|---|---|---|---|---|
| F-01 | `title` і `description` чужих інцидентів | Розкриття даних інших користувачів | Висока (`GET` без входу) | P1 | Fixed | `SearchAccessTests.*` |
| F-03 | Зміна інцидентів, `Incident.Status`, стан сеансу | Несанкціонована зміна стану з браузера жертви | Низька-середня (потрібен активний сеанс; `SameSite=Lax` частково знижує ризик) | P2 | Fixed | REG-CSRF-01 (`CsrfRegressionTests.*`) |
| F-02 | Заголовки безпеки на 500-відповідях | Послаблення defense-in-depth при помилці | Низька-середня (потрібна серверна помилка) | P3 | Fixed | REG-HDR-01 (`SecurityHeadersTests.*`) |

**Порядок виправлення:** першою закривала F-01, бо це прямий витік чужих даних без жодної автентифікації в обхід матриці ЛР 4. Другою - F-03, оскільки вона стосується зміни стану й привілейованих дій (`triage`, `DELETE`), а виправлення системно закриває одразу 5 маршрутів через один фільтр. Останньою - F-02, бо вона має найнижчий практичний вплив (відповідь 500 віддає JSON).

## 6. Детальні картки проблем

Окремі картки з відтворенням, причиною, виправленням, ретестом і доказами:
- `docs/findings/F-01-anonymous-search-disclosure.md`
- `docs/findings/F-02-security-headers-lost-on-500.md`
- `docs/findings/F-03-missing-antiforgery-on-cookie-mutations.md`

## 7. Результати регресійного тестування

Фінальний прогін після скидання БД виконано на коміті `3b08dd3` (20.11.2026) командою `dotnet test SecureLab.sln -c Release` - **PASS (63/63 passed, 0 failed)** (`docs/evidence/lr6-tests.png`).

| ID | Властивість | Умови | Негативна перевірка | Позитивна перевірка | Тести / перевірка | Результат |
|---|---|---|---|---|---|---|
| T-02 (ЛР 2) | Серверна валідація | login + token | `CreateIncidentTests.Post_InvalidSeverity_Returns400_T02` (severity «7» -> 400) | Валідний `POST` -> 201; опис від 40 символів -> 201 | `CreateIncidentTests` | PASS |
| T-03 (ЛР 2) | Конфлікт дубліката | login + token | `Post_DublicateTitle_Returns409_T03` | Унікальний `title` -> 201 | `CreateIncidentTests` | PASS |
| S-02, T-04 (ЛР 2) | Захист від SQLi у пошуку | seed, login | Контрольний ввід не змінює SQL | Апостроф і звичайний пошук повертають коректний JSON | `SearchMechanicsTests.Search_AsAuthenticatedUser_ReturnsJson` | PASS |
| A-01, A-02, A-06, A-07 (ЛР 3) | Життєвий цикл сеансу | тестовий акаунт | Анонім -> 401, після logout -> 401, невідомий юзер / хибний пароль -> однакова 401 | Після login `/api/me` повертає поточного користувача | `AuthenticationMechanicsTests` | PASS |
| A-03…A-12 (ЛР 4) | Авторизація за власником і роллю | Alice, Bob, Morgan | Доступ до чужих деталей, update, delete, triage від Reporter, чужі коментарі -> 403 | Власні операції, triage від Analyst, ігнорування підкинутих полів (A-08a) | `AuthorizationTests`, `IncidentMutationTests`, `IncidentTriageTests`, `IncidentChildResourceTests`, `IncidentEndpointTests` | PASS |
| REG-XSS-01 (ЛР 5) | Текст не виконується як код | локальний маркер | Небезпечні HTML-синки відсутні (`ClientScript_DoesNotUseDangerousInnerHtmlSink`) | Текст із спецсимволами виводиться як звичайний текст | `IncidentEndpointTests` + перевірка ЛР 5 | PASS |
| REG-CORS-01 (ЛР 5) | Точний origin у CORS | Dev (4173/4174) + Prod | Чужий origin (4174), метод `PUT` у preflight, `Production` - без CORS-дозволу | Дозволений origin `4173` у Dev отримує `Allow-Origin` і `Allow-Credentials` | `CorsRegressionTests` | PASS |
| **REG-CSRF-01** (ЛР 6) | Перевірка CSRF-токена | login + reset | Усі 5 змінювальних маршрутів без токена або з битим токеном -> 400, стан не змінюється | Із валідним токеном зміни проходять; `logout` завершує сеанс | `CsrfRegressionTests` + ручна перевірка в PowerShell і Chrome | PASS |
| Rate limit login (ЛР 3) | Обмеження спроб входу | 10 / 10 с | 11-й запит -> 429 без витоку даних | Після завершення вікна вхід працює | `LoginRateLimitTests` | PASS |
| **REG-RATE-SEARCH-01** (ЛР 6) | Ліміт пошуку та ізоляція | Alice і Bob, окремий host | 11-й запит Alice -> 429 | Запит Bob проходить (200), Alice відновлюється після 10,5 с | `SearchRateLimitTests` | PASS |
| **REG-SEARCH-ACL-01** (ЛР 6, F-01) | Авторизація пошуку | анонім, Alice, Bob, Morgan, admin | Анонім -> 401, admin -> 403, Bob не бачить інцидент Alice | Alice бачить свій, Morgan бачить усі | `SearchAccessTests` | PASS |
| **REG-HDR-01** (ЛР 6, F-02) | Заголовки безпеки на 5 класах | Dev + Prod host | 500 у Production зберігає заголовки і не розкриває стек | Статичні сторінки, 200, 400, 404 мають усі 3 заголовки | `SecurityHeadersTests` + `curl.exe -i` | PASS |
| **REG-LOG-01** (ЛР 6) | Чистота логів і audit | in-memory sink + файл логу | Паролів, cookie й токенів немає | Присутні audit-події (login, denied, status transition, csrf rejected) | `LogHygieneTests` + `Select-String` | PASS |

## 8. Перевірка механізмів захисту (Hardening controls)

### 8.1 Заголовки безпеки

| Клас відповіді | Профіль / клієнт | Запит | Статус | Фактично отримано | Ризик | Межа |
|---|---|---|---|---|---|---|
| Статична сторінка | Development, `SecurityHeadersTests` / Chrome | `GET /` | 200 | `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `CSP: default-src 'self'; frame-ancestors 'none'` | MIME sniffing, витік referrer, clickjacking | Не замінює коректний `Content-Type` |
| Успішний API | Development, Alice | `GET /api/me` | 200 | Усі три заголовки присутні | Те саме | Не замінює авторизацію |
| Валідація 4xx | Development, Alice | `GET /api/incidents?status=Unknown` | 400 | Усі три заголовки присутні | Те саме | |
| 404 | Development, Alice | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 | Усі три заголовки присутні | Те саме | |
| Контрольована 500 | Production, `curl.exe -i` (БД на порту 54399) | `POST /api/auth/login` | 500 | Після фіксу F-02 усі три заголовки присутні (`lr6-f02-after.png`) | Те саме | Перевірено один сценарій 500 |

Після підключення `csrf.js` з того самого origin перевірила в Chrome сторінки `auth-lab.html` і `browser-lab.html` - CSP `default-src 'self'; frame-ancestors 'none'` нічого не блокує, скрипти працюють коректно. Cookie автентифікації має `HttpOnly`, `SameSite=Lax`, `Secure` поза `Development` (`CookieFlagsTests`); antiforgery cookie - `HttpOnly`, `SameSite=Strict`, `Secure` поза `Development`.

### 8.2 CORS

Політика `Lab05BrowserProbe` активна лише в `Development`: один origin `http://localhost:4173`, метод `GET`, заголовок `X-Lab-Cors-Probe`, credentials дозволено без wildcard. Автоматизована перевірка `CorsRegressionTests` пройшла (**PASS**, у тому числі перевірка відсутності `PUT` у `Access-Control-Allow-Methods` та відсутності CORS у `Production`). Межа: CORS не є авторизацією і не захищає від CSRF; HTTP-тест перевіряє лише заголовки відповіді, а не поведінку браузера.

### 8.3 Змінювальні маршрути та antiforgery

Усі змінювальні маршрути на сервері звірені з клієнтськими скриптами. Механізм захисту: заголовок **`X-CSRF-TOKEN`**, видача токена через `GET /api/auth/csrf-token` (лише після входу), перевірка через спільний `AntiforgeryValidationFilter` (повертає `400 Problem Details`). `DisableAntiforgery()` ніде не використовувався.

| ID | Маршрут | Джерело / примітка | Зміна стану | Auth cookie | Доступний з браузера | Antiforgery | Перевірка |
|---|---|---|---|---|---|---|---|
| SC-01 | `POST /api/auth/register` | ЛР 3; `auth-lab.js`, `register-check.js` | Створює користувача | Ні | Так | Ні (анонімний запит; прийнятий ризик, розд. 10) | `RegisterTests` |
| SC-02 | `POST /api/auth/login` | ЛР 3; `auth-lab.js`, `probe-check.js` | Створює cookie-сеанс | Ні | Так | Ні (до входу токена немає; діє rate limit) | `LoginRateLimitTests`, A-07 |
| SC-03 | `POST /api/auth/logout` | ЛР 3; `auth-lab.js` | Завершує сеанс | Так | Так | Так (фільтр до `SignOutAsync`) | `CsrfRegressionTests` |
| SC-04 | `POST /api/incidents` | ЛР 2; `auth-lab.js`, `browser-lab.js` | Створює інцидент | Так | Так | Так | `CsrfRegressionTests` |
| SC-05 | `PUT /api/incidents/{id}` | ЛР 4; немає кнопки в UI | Оновлює інцидент | Так | Так | Так | `CsrfRegressionTests` |
| SC-06 | `DELETE /api/incidents/{id}` | ЛР 4; немає кнопки в UI | Видаляє інцидент | Так | Так | Так | `CsrfRegressionTests` |
| SC-07 | `POST /api/incidents/{id}/triage` | ЛР 4; немає кнопки в UI | Переводить `Status` у `Triaged` | Так | Так | Так | `CsrfRegressionTests`, `IncidentTriageTests` |
| - | `GET /api/auth/csrf-token` | ЛР 6 | Ставить лише antiforgery cookie | Так | Так | Не потрібно | `TokenRoute_Anonymous_Returns401` |

Маршрути SC-05…SC-07 залишено на сервері як частину матриці ЛР 4 і захищено тим самим фільтром незалежно від наявності UI.  
**Ручна перевірка в Chrome після впровадження:** на `auth-lab.html` виконала `login` -> створення інциденту (`201`) -> `F5` -> повторне створення (`201`) -> `logout` -> повторний `login` -> створення (`201`, у DevTools видно заголовок `X-Csrf-Token`, скрін `docs/evidence/lr6-browser-csrf.png`); на `browser-lab.html` створення інциденту також повернуло `201`.

### 8.4 HTTPS і HSTS

У `launchSettings.json` використовується локальний профіль `http://localhost:5080`; `UseHttpsRedirection()` і `UseHsts()` у `Program.cs` не додавалися. Поза `Development` для auth- та antiforgery-cookie виставляється `SecurePolicy.Always`. Локальний HTTP-стенд не доводить захищеність транспорту в production - перевірка TLS/HTTPS і HSTS залишається поза межами стенда.

### 8.5 Rate limiting

| Сценарій | Ключ (Partition) | Ліміт | Відповідь при перевищенні | Перевірка |
|---|---|---|---|---|
| `login` (ЛР 3) | IP (`RemoteIpAddress`) | 10 запитів / 10 с, без черги | `429`, без імені користувача й пароля в тілі | `LoginRateLimitTests` (PASS) |
| `search` (ЛР 6) | `NameIdentifier` користувача (fallback IP) | 10 запитів / 10 с, без черги | `429`, без даних інцидентів у тілі | `SearchRateLimitTests`: 11-й запит Alice -> 429, Bob -> 200, відновлення після 10,5 с (PASS) |

Rate limiting не замінює автентифікацію: анонімний запит до пошуку відхиляється з `401` ще до limiter. Обмеження реалізовано в пам’яті процесу (in-memory), тому між кількома інстансами API лічильники не спільні.

### 8.6 Порівняння Development та Production

| Параметр | Development | Production |
|---|---|---|
| `Hosting environment` | `Development` | `Production` (`lr6-prod-env.png`) |
| Connection string | `appsettings.Development.json` (навчальний, `127.0.0.1`) | Лише через `ConnectionStrings__SecureLab` |
| `ApplyMigrationsOnStartup` / `AllowReset` | `true` / `true` | `false` / `false` (`--reset-database` відмовляє, `lr6-prod-env.png`) |
| OpenAPI / Scalar | Доступні | Вимкнені (`404` / SPA fallback) |
| CORS `Lab05BrowserProbe` | Активна | Вимкнена (`CorsRegressionTests.Production_*`) |
| Cookie `SecurePolicy` | `SameAsRequest` | `Always` |
| CSP на `/scalar` | Пропущена для UI Scalar | Застосовується всюди |
| Відповідь 500 | Problem Details без стеку | Problem Details із `traceId`, без стеку (`lr6-500-log.png`) |
| `AllowedHosts` | `*` | `*` (залишковий ризик, розд. 10) |

У `Production` перевірила запуск із недоступною БД (порт `54399`): у консолі зафіксовано `Hosting environment: Production`, спроба `--reset-database` відхилена (`lr6-prod-env.png`), `POST /api/auth/login` повернув `500` Problem Details із `traceId`, якому відповідає запис `ExceptionHandlerMiddleware` у лозі без стеку й паролів (`lr6-500-log.png`), а після фіксу F-02 у відповіді присутні всі заголовки безпеки (`lr6-f02-after.png`).

### 8.7 Відмінний рівень

Окремі вимоги відмінного рівня (розширена CSP-валідація, обов’язковий e2e REG-CSRF-01 як критерій рівня, додаткова finding поза чеклістом, advanced variant) **не входять до обраного рівня**.

## 9. Аудит залежностей, секретів і журналів

### 9.1 Конфігурація та пошук секретів у tracked-файлах

Перевірила поточні tracked-файли в репозиторії (`git ls-files`, `git check-ignore`, `git grep`). Файли `infra/.env` та `appsettings.Local.json` коректно ігноруються через `.gitignore`.  
Пошук командою `(git grep -n -I -i -E "password|passwd|secret|api.?key" -- . | Measure-Object).Count` на поточному стані дав **112 збігів** (`docs/evidence/lr6-grep.png`). Усі збіги перевірено за контекстом:
- `infra/compose.yaml`, `appsettings.Development.json`, `SecureLabDesignTimeDbContextFactory.cs`, `README.md` - відкритий навчальний пароль БД `local-study-password` для `127.0.0.1` (**прийнято як study-only**, не видаляти, щоб не зламати локальний стенд);
- `DbSeeder.cs`, `Lab03Identity.cs`, міграції - назви полів і ключів конфігурації (`SeedUsers:Password`, `PasswordHash`, `password_hash`), реальні значення передаються через змінні середовища;
- `tests/`, `Client/`, `docs/` - читання `SeedUsers__Password` у тестах, поля форм `type="password"` та описи в документації.

Реальних секретів у tracked-файлах не виявлено. Межа: історія Git, stash і untracked-файли не перевірялися; у разі реального витоку видалення з файлу недостатньо - потрібна ротація секрету.

### 9.2 Аудит залежностей (NuGet)

20.11.2026 на SDK `10.0.403` виконала `dotnet package list --vulnerable --include-transitive` (`docs/evidence/lr6-audit.png`).  
Фактичний результат:
- `SecureLab.Api`: **no vulnerable packages given the current sources**;
- `SecureLab.Api.Tests`: **no vulnerable packages given the current sources**.

| Пакет | Версія | Тип | Advisory | Рішення |
|---|---|---|---|---|
| `Microsoft.OpenApi` | `2.7.5` | direct (override транзитивної залежності) | Не виявлено (пропатчена версія вже зафіксована в `.csproj` і lock-файлі) | Залишити, оновлення не потрібне |
| `Microsoft.AspNetCore.OpenApi`, `Identity.EntityFrameworkCore`, `EntityFrameworkCore` (+Relational, Design) | `10.0.10` | direct | Не виявлено | Залишити |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `10.0.3` | direct | Не виявлено | Залишити |
| `Scalar.AspNetCore` | `2.16.16` | direct (лише Development) | Не виявлено | Залишити |
| Тестові пакети (`SecureLab.Api.Tests`) | за `.csproj` | direct (не потрапляють у runtime) | Не виявлено | Залишити |

Межа висновку: перевірено лише відомі на 20.11.2026 advisories із джерела NuGet; невідомі вразливості й досяжність коду не оцінювалися. Придушення попереджень (`NuGetAuditSuppress`) не використовувалися.

### 9.3 Журнали та audit-події

| Сценарій | Що пишеться в лог | Чого немає | Перевірка |
|---|---|---|---|
| Успішний / невдалий login | `Auth event login: result success/failure, trace` | username, password, cookie | `LogHygieneTests`, перевірка логу |
| Відмова авторизації | `authorization.denied subject action resource result occurredAtUtc traceId` | чужі дані, тіло запиту, cookie | `LogHygieneTests` |
| Помилка валідації (400) | `400 Problem Details`, у лозі лише статус-фільтр | повне тіло запиту | `IncidentQueries`, перегляд логу |
| Помилка 500 | `ExceptionHandlerMiddleware` + `TraceId` | стек у відповіді, connection string, пароль | `lr6-500-log.png`, `SecurityHeadersTests` |
| Привілейований triage | `audit.status_transition subject action resource from to result occurredAtUtc traceId` + запис у `IncidentStatusHistory` | note, cookie, token | `LogHygieneTests`, `IncidentTriageTests` |
| Відхилений CSRF | `csrf.rejected method route traceId` | токен, cookie, тіло | `LogHygieneTests` |

Ручна перевірка зібраного файлу логів `..\lr06-logs\*.log` у PowerShell через `Select-String` (`docs/evidence/lr6-log-check.png`):
- збігів із `$env:SeedUsers__Password`: **0**;
- збігів із `'NotARealPassword1!'`: **0**;
- збігів із `'cookie:|set-cookie'`: **0**.

У логах EF Core чутливі параметри не логуються (`EnableSensitiveDataLogging` не вмикалося).

## 10. Залишкові ризики

| Ризик | Відповідальний | Що робити далі |
|---|---|---|
| Немає перевіреного HTTPS/HSTS (локальний HTTP) | Автор | Перевірити на HTTPS-профілі перед деплоєм |
| `login` і `register` без CSRF-токена; `register` без rate limit | Автор | За потреби додати анонімний токен і ліміт на реєстрацію |
| `AllowedHosts: *` у `appsettings.json` | Автор | Обмежити конкретними хостами перед деплоєм |
| In-memory rate limit не спільний між екземплярами; ключ login - IP | Автор | Для продакшену винести лічильники в Redis |
| Antiforgery не захищає від XSS на тому самому origin | Автор | Підтримувати безпечний вивід (`textContent`) і CSP |
| Не перевірялися історія Git, stash і невідомі вразливості пакетів | Автор | Періодично повторювати аудит залежностей |
| Перевірка заголовків на 500 покриває один тип помилки | Автор | За потреби додати інші сценарії помилок |

## 11. Висновок

У межах ЛР 6 виправлено 3 знайдені проблеми безпеки (F-01, F-02, F-03), додано CSRF-захист, rate limiting пошуку та audit-логування переходу статусу. Повний регресійний прогін `dotnet test SecureLab.sln -c Release` на коміті `3b08dd3` (20.11.2026) пройшов успішно (**63/63 passed**). Усі перевірки підтверджені скріншотами та тестами; межі огляду й залишкові ризики зафіксовані в розділах 2 і 10.

## 12. Додаток: перелік доказів

- Документація й картки: `docs/threat-model.md`, `docs/access-matrix.md`, `docs/findings/F-01-*.md`, `F-02-*.md`, `F-03-*.md`.
- Тести: `tests/SecureLab.Api.Tests/*`.
- Власні скріншоти в `docs/evidence/`:
  - `lr6-prod-env.png`, `lr6-prod-headers.png`, `lr6-500-log.png` - перевірка Production-режиму, відмови `--reset-database` і зв’язку `traceId` у відповіді та лозі;
  - `lr6-f01-before.png`, `lr6-f01-after.png` - F-01 до та після виправлення;
  - `lr6-f02-before.png`, `lr6-f02-after.png` - F-02 до та після виправлення;
  - `lr6-f03-before.png`, `lr6-f03-after-400.png`, `lr6-f03-after-201.png`, `lr6-browser-csrf.png` - F-03 у PowerShell і Chrome DevTools (значення токена приховано);
  - `lr6-tests.png` - прогін `dotnet test SecureLab.sln -c Release` (63/63 passed);
  - `lr6-log-check.png` - перевірка логів на відсутність паролів і cookie (усі 0);
  - `lr6-audit.png` - `dotnet --version` (`10.0.403`) і `dotnet package list --vulnerable --include-transitive` (0 вразливих пакетів);
  - `lr6-grep.png` - підрахунок збігів `git grep` (`112`).

  ## Декларація використання ШІ

Під час виконання ЛР 6 використовувала генеративний ШІ як асистента для чернеток змін у коді (antiforgery, перенесення middleware заголовків, обмеження пошуку, rate limit), заготовок тестів і структури документації. Усі зміни переглядала й адаптувала самостійно: підправила 3 тести під фактичну поведінку API (перевірки полів у 401/429 та методу `PUT` у CORS). Усі ручні перевірки в PowerShell і Chrome, аудит пакетів, перевірку логів, прогін тестів (63/63) та збір скріншотів виконала самостійно.

| Дата | Що давав ШІ | Що адаптувала й перевірила сама | Результат |
|---|---|---|---|
| 19–20.11.2026 | Чернетки фіксів F-01–F-03, тестів і звітів ЛР 6 | Адаптувала код і коментарі, виправила 3 падаючі тести, переписала звіти під реальні запуски, провела всі ручні перевірки в PowerShell і Chrome | `dotnet test -c Release`: 63/63 passed, усі скріни в `docs/evidence/` |