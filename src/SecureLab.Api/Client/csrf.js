// спільний helper для csrf-токена; тримаємо токен тільки в пам'яті сторінки (не в storage і не в cookie), після F5 запитуємо заново
(() => {
    const HEADER_NAME = "X-CSRF-TOKEN";   // має збігатись із заголовком на сервері
    const TOKEN_ROUTE = "/api/auth/csrf-token";
    let token = null;
    let pending = null;
    let generation = 0;

    // скидаємо кеш токена після login/logout, бо токен прив'язаний до поточного сеансу
    function reset() {
        token = null;
        pending = null;
        generation++;
    }

    async function ensureToken() {
        if (token) return token;
        if (pending) return pending;
        const current = generation;
        const operation = (async () => {
            const response = await fetch(TOKEN_ROUTE, { credentials: "same-origin", cache: "no-store" });
            if (!response.ok) {
                throw new Error(`Token request failed: ${response.status}`);
            }
            const data = await response.json();
            if (current !== generation) {
                throw new Error("Session changed; request a fresh token.");
            }
            if (typeof data.token !== "string" || !data.token) {
                throw new Error("Invalid token response.");
            }
            token = data.token;
            return token;
        })();
        pending = operation;
        try { return await operation; }
        finally { if (pending === operation) pending = null; }
    }

    // єдина точка для POST/PUT/PATCH/DELETE. повторно запит автоматично НЕ надсилається
    async function mutate(url, options = {}) {
        const method = (options.method || "POST").toUpperCase();
        if (!["POST", "PUT", "PATCH", "DELETE"].includes(method)) {
            throw new Error("Use normal fetch for read requests.");
        }
        const target = new URL(url, location.href);
        if (target.origin !== location.origin) {
            throw new Error("Token must stay on the same origin.");
        }
        const headers = new Headers(options.headers);
        headers.set(HEADER_NAME, await ensureToken());
        const response = await fetch(target, { ...options, method, headers, credentials: "same-origin" });
        if (response.status === 400 || response.status === 401) reset();
        return response;
    }

    window.SecureLabCsrf = { reset, ensureToken, mutate };
    window.addEventListener("pageshow", reset);
})();
