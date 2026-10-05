# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасуванням запиту,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку або деталей інциденту |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Підготовлений наскрізний маршрут

```text
submit/click у Client/app.js
  → GET /api/incidents або GET /api/incidents/{id}
  → Presentation/Endpoints/IncidentEndpoints.cs
  → Application/Incidents/IncidentQueries.cs
  → Data/SecureLabDbContext.cs
  → PostgreSQL
  → response DTO у Presentation/Contracts/
  → JSON
  → textContent/createTextNode у Client/app.js
```

## Власний маршрут для підсумку за severity 

Endpoint `GET /api/incidents/severity-summary` працює за тим самим принципом,
що й готовий приклад із фільтром за статусом:

```text
клік "Показати підсумок" у Client/app.js
 → GET /api/Incidents/severity-summary[?status=...]
 → Presentation/Endpoints/IncidentEndpoints.cs (GetSeveritySummaryAsync)
 → Application?Incidents/IncidentQueries.cs (GetSeveritySummaryAsync)
 → dbContext.Incidents.GroupBy(Severity) → PotgreSQL BROUP BY
 → IncidentSeveritySummaryResponse[] у Presentation/Contracts/
 → JSON
 → textContent у #summary-list ()Client/app.js
 ```
 Віповідь завжди містить усі 4 рівні severity, навіть якщо для якогось з них у БД 
 немає жодного інциденту (тоді `count: 0`) - так простіше відобразити список на клієнті,
 не перевіряючи окремо, чого бракує. Сортування зроблено за критичністю, від найбільш критичного 
 до найменш, а не за алфавітом.

 Параметр `status` необов'язковий і пряцює так само, як і в списку інцидентів: якщо передати щось,
 чого немає серед відомих статусів, повертається `400` з описом помилки, а не падіння сервера
 
## Межі довіри

Доповніть таблицю щонайменше трьома конкретними спостереженнями.

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач вводить дані через форму (фільтр статусу) | `<select>` обмежує вибір фіксованим списком значень |
| Browser client → API | HTTP-запит можна відпрвити й напряму, минаючи UI (напр., вписати `?status=qwerty` в адресний рядок) | Сервер сам перевіряє `status` через `Enum.TryParse`, повертає `400` при некоректному значенні|
| PostgreSQL → API → DOM | В базі може зберігатися текст, введений раніше, (напр., `<script>` в описі інциденту| Дані виводяться через `textContent`, а не `innerHTLM`, тому такий текст не виконаэться як HTML |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.
