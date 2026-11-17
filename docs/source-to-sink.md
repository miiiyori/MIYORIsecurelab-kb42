# Власна карта source-to-sink

Поле: Incident.Description. Стан/commit: вразливий стан 81c553a (scaffold lab-05-start-v1), виправлення відображення 5f8e3c8, підсумковий стан ae46fc2. Вхід/Guid запису: af258e34-d4f9-4e9f-b64e-8cc3282ce3b0, інцидент створила Alice, а прочитав Morgan. 

## Потік Incident.Description

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | Client/browser-lab.html, форма #edit | поле description, значення читається як `event.currentTarget.elements.description.value` | Текст опису вводить користувач, тому значення повністю визначає він. Довіри до нього немає. |
| Request | Client/browser-lab.js, обробник submit | запит POST /api/incidents із JSON-полем description | Запит надсилається з cookie Alice, а саме значення клієнт не змінює. |
| Validation/mapping | IncidentEndpoints.cs | перевірка обов'язковості та довжини (до 4000 символів) | Рядок із розміткою цю перевірку проходить ([рис. 6](evidence/lab05-before-html-bold.png)), бо вона оцінює форму значення, а не те, як його згодом виводитимуть. |
| Storage | сутність Incident, поле Description (PostgreSQL) | рядок записується без змін | Після повторного читання в JSON бачимо той самий рядок із marker ([рис. 5](evidence/lab05-before-description-json.png)), тому збережене значення і далі вважаю недовіреним. |
| Response | GET /api/incidents/{id} | поле description у JSON-відповіді | JSON повертає рядок дослівно ([рис. 5](evidence/lab05-before-description-json.png)). |
| Client transform | Client/browser-lab.js, функція load() | `current = await response.json()` | Інших перетворень немає: значення не екранується й не очищується. |
| Sink/context | Client/browser-lab.js, load(), елемент #description | до fix `innerHTML = current.description`, після fix `renderPlainText(...)`, який використовує textContent | До виправлення браузер розбирав рядок як HTML, і marker виконувався вже в сеансі Morgan ([рис. 4](evidence/lab05-before-description-morgan.png)). Після виправлення в Elements лишається один текстовий вузол ([рис. 8](evidence/lab05-after-description-dom.png)). |

## Потік IncidentComment.Text

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | Data/DbSeeder.cs, метод SeedAsync | Text публічного seed-коментаря з marker (Guid 30000000-0000-0000-0000-000000000005, IsInternal = false, автор Morgan, інцидент 20000000-0000-0000-0000-000000000001) | Для цієї роботи я додала коментар у seed (коміт 918aec0, [рис. 2](evidence/lab05-git-history.png)). За змістом це текст, який написав би користувач, тому довіри до нього немає. |
| Request | не застосовується | у клієнті немає форми додавання коментаря | Значення потрапляє в базу через seed, а не через інтерфейс. |
| Validation/mapping | IncidentQueries.GetDetailsAsync | перетворення коментарів на масив comments | Діє правило видимості з ЛР 4: публічний коментар бачать власник інциденту та Analyst. |
| Storage | сутність IncidentComment, поле Text | рядок із marker | Значення не змінюється ([рис. 3](evidence/lab05-before-comment-dom.png)). |
| Response | GET /api/incidents/{id} | поле comments[].text | Коментар повертається в масиві comments. |
| Client transform | Client/browser-lab.js, цикл у load() | `comment.text` | Перетворень немає. |
| Sink/context | Client/browser-lab.js, load(), елемент p у #comments | до fix `paragraph.innerHTML = comment.text`, після fix `renderPlainText(...)` | До виправлення в #comments з'являвся елемент img, а marker виконувався ([рис. 3](evidence/lab05-before-comment-dom.png)). Після виправлення там текстовий вузол ([рис. 10](evidence/lab05-after-comment-dom.png)), а перевірка marker повертає null ([рис. 11](evidence/lab05-after-comment-marker-null.png)). |

## Інвентаризація виведення

| Файл і функція | Категорія даних | Контекст | Рішення |
|---|---|---|---|
| browser-lab.js, load(): title | назва інциденту | текст (`textContent`) | безпечно, залишила без змін |
| browser-lab.js, load(): description | опис інциденту | було HTML (`innerHTML`) | замінила на renderPlainText |
| browser-lab.js, load(): comment.text | текст коментаря | було HTML (`innerHTML`) | замінила на renderPlainText |
| browser-lab.js, форма description | опис інциденту | властивість `value` поля textarea, а не HTML | безпечно, залишила без змін |
| browser-lab.js: рядок статусу | службове повідомлення | текст (`textContent`) | безпечно, залишила без змін |
| app.js (головна сторінка) | поля інциденту й коментарі | `textContent` та `createTextElement` | безпечно; відсутність innerHTML підтверджує тест ClientScript_DoesNotUseDangerousInnerHtmlSink |
| auth-lab.js | дані профілю й повідомлення | пошук за `innerHTML`, `outerHTML`, `insertAdjacentHTML` і `document.write` у Client/*.js нічого не знайшов | безпечно, залишила без змін |