# F-03. Змінювальні маршрути з cookie-автентифікацією не перевіряють CSRF-токен

**Статус:** Fixed  
**Що під загрозою:** зміна та видалення інцидентів користувача, привілейований перехід `Incident.Status` (triage), стан сеансу  
**Компонент:** `POST /api/incidents`, `PUT`/`DELETE /api/incidents/{id}`, `POST /api/incidents/{id}/triage`, `POST /api/auth/logout`  
**Пріоритет:** P2

## Умови перевірки

База - тег `v0.5.0` (`09667b6`), гілка `lab/6-hardening-review`. Налаштування auth-cookie не змінювалися (`HttpOnly`, `SameSite=Lax`, `Secure` поза `Development`). Перевіряла на рівні HTTP-запитів із дійсним cookie-сеансом без передачі CSRF-токена. Реальну експлуатацію зі стороннього сайту в браузері не відтворювала.

## Відтворення до виправлення

1. Увійшла як Alice (отримала дійсний cookie-сеанс).
2. Надіслала змінювальний запит до API з чинним cookie, але без жодного antiforgery-токена.
3. **Очікувано:** сервер має відхилити запит із помилкою `4xx`, а стан у базі не повинен змінюватися.
4. **Фактично:** запит успішно виконався, оскільки жоден із змінювальних маршрутів не перевіряв CSRF-токен (зафіксовано на скріншоті `docs/evidence/lr6-f03-before.png`).

## Вплив і ймовірність

Підтверджено на HTTP-рівні локальним запуском на `v0.5.0` (реальна cross-site атака в браузері не перевірялася). Оскільки браузер автоматично підставляє auth-cookie, сервер не може покладатися лише на наявність сеансу як доказ свідомої дії користувача. `SameSite=Lax` і JSON-тіло запитів суттєво знижують ризик, але не замінюють серверну перевірку токена, а CORS не зупиняє саме відправлення простих запитів. Для `triage` і `DELETE` це означає ризик несанкціонованої зміни стану; імовірність низька-середня (потрібен активний сеанс жертви), тому пріоритет P2.

## Причина

На змінювальних маршрутах, які працюють через cookie-автентифікацію, не було серверної перевірки antiforgery-токена - захист фактично тримався лише на атрибутах cookie.

## Виправлення

Підключила стандартний ASP.NET Core antiforgery (`AddAntiforgery`): заголовок `X-CSRF-TOKEN`, cookie `SecureLab.Antiforgery` (`HttpOnly`, `SameSite=Strict`, `Secure` поза `Development`). Додала маршрут `GET /api/auth/csrf-token` (доступний лише після входу) і спільний фільтр `AntiforgeryValidationFilter`, який ловить `AntiforgeryValidationException` і повертає `400 Problem Details`. Фільтр повісила на всі п'ять змінювальних маршрутів (для `logout` - до `SignOutAsync`). На клієнтських сторінках підключила спільний `Client/csrf.js`. Маршрути `login` і `register` не використовують auth-cookie, тому винесені в окреме рішення в `security-review.md`.  
Коміт із виправленням: `3b08dd3`.

## Повторна перевірка (Retest)

* Ручна перевірка в PowerShell після виправлення: запит із cookie без токена відхиляється з `400` (`docs/evidence/lr6-f03-after-400.png`), а після отримання пари токенів через `/api/auth/csrf-token` той самий запит проходить із `201` (`docs/evidence/lr6-f03-after-201.png`).
* Автоматизовані тести: `CsrfRegressionTests` (`RegCsrf01_PutWithoutToken_IsRejected_StateUnchanged_ThenValidPairSucceeds` та `AllCookieMutations_WithoutOrWithInvalidToken_AreRejected_AndStateIsUnchanged` - перевіряють усі п'ять маршрутів без токена і з невалідним токеном).  
  Команда: `dotnet test SecureLab.sln -c Release`  
  Результат (20.11.2026): **PASS** (усі 63/63 тести пройшли).

## Перевірка робочих сценаріїв

Перевірила вручну в браузері: на `auth-lab.html` працює вхід, створення інциденту (`201`), повторне створення після `F5`, вихід і повторний вхід зі створенням (`201`, у DevTools видно заголовок `X-Csrf-Token` - `docs/evidence/lr6-browser-csrf.png`); на `browser-lab.html` створення інциденту також повертає `201`. Існуючі тести ЛР 2-5 проходять через оновлений `CookieClient.LoginAsync`, тест `CsrfRegressionTests.Logout_WithValidToken_Returns204_AndSessionEnds` - **PASS**.

## Залишковий ризик

`login` і `register` не захищені CSRF-токеном (там ще немає cookie-сеансу; для навчального стенда це прийнятий ризик). Antiforgery не замінює авторизацію і не захищає від XSS на тому самому origin, бо скрипт на сторінці зможе прочитати токен. Окремий таймаут життя токена не налаштовувався.

## Докази

Тести `CsrfRegressionTests.*`, скріншоти `docs/evidence/lr6-f03-before.png`, `docs/evidence/lr6-f03-after-400.png`, `docs/evidence/lr6-f03-after-201.png` та `docs/evidence/lr6-browser-csrf.png` (значення токена замальовано).