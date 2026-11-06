const result = document.querySelector("#result");
const profile = document.querySelector("#profile");
const state = document.querySelector("#state");

function clearProfile() {
    profile.textContent = "";
    state.textContent = "Не ввійшли";
}

async function loadProfile() {
    const response = await fetch("/api/me");
    if (response.status === 401) {
        clearProfile();
        return 401;
    }
    if (!response.ok || !(response.headers.get("Content-Type") ?? "").includes("application/json")) {
        profile.textContent = "";
        state.textContent = `Помилка профілю: ${response.status}`;
        return response.status;
    }
    const data = await response.json();
    profile.textContent = `Ім'я користувача: ${data.userName}\nВідображуване ім'я: ${data.displayName}`;
    state.textContent = "Сеанс активний";
    return response.status;
}

// 1. REGISTER
document.querySelector("#register").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;

    const response = await fetch("/api/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(Object.fromEntries(new FormData(form)))
    });

    form.elements.password.value = "";

    // якщо успішно, пропонуємо увійти
    result.textContent = response.status === 201
        ? "Register: 201. Тепер увійдіть."
        : `Register: ${response.status}`;
});

// 2. LOGIN
document.querySelector("#login").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(Object.fromEntries(new FormData(form)))
    });

    form.elements.password.value = "";
    result.textContent = `Login: ${response.status}`;

    if (response.status === 204) {
        await loadProfile();
    }
});

document.querySelector("#me").addEventListener("click", loadProfile);

// 3. CREATE
document.querySelector("#incident").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);

    const dto = {
        title: data.get("title"),
        description: data.get("description"),
        severity: data.get("severity"),
        occurredAtUtc: new Date(data.get("occurredAtUtc")).toISOString()
    };

    const response = await fetch("/api/incidents", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(dto)
    });

    result.textContent = `Create: ${response.status}`;
});

// 4. LOGOUT
document.querySelector("#logout").addEventListener("click", async () => {
    const response = await fetch("/api/auth/logout", { method: "POST" });

    if (response.status !== 204) {
        result.textContent = `Logout: ${response.status}`;  // вихід не вдався, нічого не чистимо
        return;
    }

    document.querySelector("#incident").reset();            // прибираємо введене у формі інциденту
    clearProfile();
    const meStatus = await loadProfile();                   // повторна перевірка тим самим браузером
    result.textContent = `Logout: 204; повторний /api/me: ${meStatus}`;
});

// стан при відкритті: якщо cookie вже є, покажемо активний сеанс, інакше анонімний
loadProfile().catch(() => { state.textContent = "Стан невідомий"; });