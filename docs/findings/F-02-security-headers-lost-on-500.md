# F-02. Заголовки безпеки зникають на 500-відповіді

**Статус:** Fixed  
**Що під загрозою:** базові заголовки захисту браузера (`CSP`, `X-Content-Type-Options`, `Referrer-Policy`) на відповідях із серверною помилкою  
**Компонент:** порядок middleware у `Program.cs`  
**Пріоритет:** P3

## Умови перевірки

База - тег `v0.5.0` (`09667b6`), гілка `lab/6-hardening-review`. Запускала API у режимі `Production` (`ASPNETCORE_ENVIRONMENT=Production`, `--no-launch-profile`) із недоступним портом БД (`54399`) і надсилала `POST /api/auth/login` із вигаданим паролем; також перевіряла через тестовий host із `UseEnvironment("Production")`. Інші типи необроблених помилок та HTTPS/HSTS тут не перевірялися.

## Відтворення до виправлення

1. На стані `v0.5.0` запустила API у `Production` із недоступною БД (порт `54399`) і виконала `curl -i` на `POST /api/auth/login` (`{"userName":"alice","password":"NotARealPassword1!"}`).
2. **Очікувано:** `500` Problem Details із `traceId`, а також `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` і `Content-Security-Policy` - так само, як на звичайних 200-відповідях.
3. **Фактично:** `500` Problem Details повернувся коректно, але без жодного з трьох заголовків безпеки, хоча на `200`-відповідях вони були присутні (зафіксовано на скріншоті `docs/evidence/lr6-f02-before.png`).

## Вплив і ймовірність

Підтверджено локальним запуском у `Production` на `v0.5.0`. Відповідь про помилку приходить у браузер без `nosniff`, `CSP` та `Referrer-Policy`. Оскільки `500` віддає JSON, практичний вплив невеликий (це радше питання додаткового шару захисту), але порушується вимога, що заголовки мають бути на всіх класах відповідей. Ймовірність низька-середня (потрібна серверна помилка), вплив низький.

## Причина

У `Program.cs` middleware заголовків стояв **після** `UseExceptionHandler()`. Коли виникав виняток, exception handler очищав заголовки відповіді перед записом Problem Details, тому виставлені раніше заголовки зникали.

## Виправлення

Перенесла middleware заголовків перед `UseExceptionHandler()` і перевела виставлення заголовків у `Response.OnStarting(...)`. Цей callback спрацьовує вже перед самою відправкою відповіді, після того як exception handler сформував Problem Details. Виняток CSP для `/scalar` у `Development` збережено.  
Коміт із виправленням: `3b08dd3`.

## Повторна перевірка (Retest)

* Автоматизований тест `SecurityHeadersTests.UnexpectedError500_InProduction_IsSafe_AndKeepsBaselineHeaders` (до фіксу падав на `AssertBaselineHeaders`, після фіксу проходить). Результат (20.11.2026): **PASS** (у складі повного прогону 63/63 passed у Release).
* Ручний `curl -i` на `POST /api/auth/login` у `Production` із недоступною БД після виправлення повертає `500` Problem Details уже з усіма трьома заголовками (`docs/evidence/lr6-f02-after.png`).

## Перевірка робочих сценаріїв

Тести `SecurityHeadersTests.StaticPage_*` та `SuccessfulApi_ValidationError_And404_*` проходять (**PASS**). Звичайна робота клієнтських сторінок у браузері (`auth-lab.html`, `browser-lab.html`) не зламалася - CSP `default-src 'self'` залишилася без змін, `/scalar` у `Development` працює.

## Залишковий ризик

Перевірено лише один сценарій 500-помилки (недоступна БД). Відповіді про помилки, які формує сам Kestrel ще до входу в middleware (наприклад, некоректний HTTP-запит), цим механізмом не покриваються.

## Докази

Тести `SecurityHeadersTests.*`, скріншоти `docs/evidence/lr6-f02-before.png`, `docs/evidence/lr6-f02-after.png` та `docs/evidence/lr6-500-log.png` (відповідь із `traceId` та рядок `ExceptionHandlerMiddleware` у лозі без стеку й паролів).