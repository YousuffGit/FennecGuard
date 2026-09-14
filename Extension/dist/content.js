"use strict";
// FennecGuard Content Script: Form Autofill, In-Field Badges & Persistent Save Prompt
(() => {
    const logoUrl = chrome.runtime.getURL("icons/logo.png");
    const STORAGE_KEY = "fennecguard_pending_credential";
    let hasAutoFilled = false;
    let bufferedUsername = "";
    let bufferedPassword = "";
    // Listen for manual Autofill commands from popup
    chrome.runtime.onMessage.addListener((request, _sender, sendResponse) => {
        if (request.action === "AUTOFILL_FORM") {
            const { username, password } = request;
            const success = executeFormAutofill(username, password);
            sendResponse({ success });
        }
    });
    // Track credentials in real-time as user types
    document.addEventListener("input", (e) => {
        if (e.target instanceof HTMLInputElement) {
            if (e.target.type === "password") {
                bufferedPassword = e.target.value;
                const uField = findUsernameField(e.target);
                if (uField && uField.value)
                    bufferedUsername = uField.value.trim();
            }
            else if (isUsernameField(e.target)) {
                bufferedUsername = e.target.value.trim();
            }
        }
    }, true);
    // Safely execute init whether page is loading or already loaded
    function init() {
        scanFields();
        checkPendingSave();
    }
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    }
    else {
        init();
    }
    const observer = new MutationObserver(() => scanFields());
    observer.observe(document.documentElement, { childList: true, subtree: true });
    // Decorate fields and attempt autofill
    function scanFields() {
        const passwordInputs = Array.from(document.querySelectorAll('input[type="password"]'))
            .filter(isSecurelyVisible);
        const usernameInputs = Array.from(document.querySelectorAll('input[type="text"], input[type="email"], input:not([type])'))
            .filter(input => isSecurelyVisible(input) && isUsernameField(input));
        const allInputs = [...passwordInputs, ...usernameInputs];
        allInputs.forEach(input => {
            if (input.dataset.fennecDecorated)
                return;
            input.dataset.fennecDecorated = "true";
            injectInFieldBadge(input);
        });
        if (allInputs.length > 0 && !hasAutoFilled) {
            attemptAutomaticAutofill();
        }
    }
    async function attemptAutomaticAutofill() {
        const currentDomain = window.location.hostname.replace("www.", "").toLowerCase();
        if (!currentDomain)
            return;
        const data = await sendToBackground("/logins");
        if (!data?.success || !Array.isArray(data.items) || data.items.length === 0)
            return;
        const matches = data.items.filter(item => {
            const itemUrl = (item.websiteUrl || item.WebsiteUrl || "").toLowerCase();
            return isDomainMatch(currentDomain, itemUrl);
        });
        if (matches.length > 0) {
            const primary = matches[0];
            const credData = await sendToBackground("/credential", "POST", { id: primary.id });
            if (credData?.success && credData.credential) {
                const filled = executeFormAutofill(credData.credential.username, credData.credential.password);
                if (filled) {
                    hasAutoFilled = true;
                }
            }
        }
    }
    function isUsernameField(input) {
        if (input.disabled || input.readOnly || input.type === "hidden" || input.type === "password" || input.type === "submit")
            return false;
        if (input.type === "email")
            return true;
        const autocomplete = (input.autocomplete || "").toLowerCase();
        if (autocomplete === "username" || autocomplete === "email")
            return true;
        const idName = (input.id + " " + input.name + " " + (input.placeholder || "")).toLowerCase();
        return idName.includes("user") ||
            idName.includes("email") ||
            idName.includes("login") ||
            idName.includes("account");
    }
    function injectInFieldBadge(input) {
        const host = document.createElement("div");
        host.style.position = "absolute";
        host.style.zIndex = "2147483647";
        host.style.pointerEvents = "auto";
        document.body.appendChild(host);
        const shadow = host.attachShadow({ mode: "open" });
        const style = document.createElement("style");
        style.textContent = `
      .badge {
        width: 22px;
        height: 22px;
        background-color: #0067C0;
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        box-shadow: 0 2px 6px rgba(0,0,0,0.35);
        cursor: pointer;
        transition: transform 0.15s ease, background-color 0.15s ease;
      }
      .badge:hover {
        background-color: #005299;
        transform: scale(1.08);
      }
      .badge img {
        width: 14px;
        height: 14px;
        object-fit: contain;
        pointer-events: none;
      }
      .dropdown {
        position: absolute;
        top: 28px;
        right: 0;
        width: 250px;
        background: #202020;
        color: #ffffff;
        border: 1px solid #383838;
        border-radius: 8px;
        box-shadow: 0 6px 18px rgba(0,0,0,0.4);
        padding: 6px;
        display: none;
        flex-direction: column;
        gap: 4px;
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
        font-size: 13px;
      }
      .item {
        padding: 8px 10px;
        border-radius: 6px;
        cursor: pointer;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }
      .item:hover {
        background: #2d2d2d;
      }
      .item-title {
        font-weight: 600;
        color: #ffffff;
      }
      .item-user {
        font-size: 11px;
        color: #aaaaaa;
      }
      .note {
        padding: 10px 8px;
        font-size: 12px;
        color: #aaaaaa;
        text-align: center;
      }
    `;
        const badge = document.createElement("div");
        badge.className = "badge";
        const img = document.createElement("img");
        img.src = logoUrl;
        badge.appendChild(img);
        const dropdown = document.createElement("div");
        dropdown.className = "dropdown";
        shadow.appendChild(style);
        shadow.appendChild(badge);
        shadow.appendChild(dropdown);
        function updatePosition() {
            if (!document.body.contains(input)) {
                host.remove();
                return;
            }
            const rect = input.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) {
                host.style.display = "none";
                return;
            }
            host.style.display = "block";
            host.style.top = `${window.scrollY + rect.top + (rect.height - 22) / 2}px`;
            host.style.left = `${window.scrollX + rect.right - 28}px`;
        }
        window.addEventListener("scroll", updatePosition, { passive: true });
        window.addEventListener("resize", updatePosition, { passive: true });
        input.addEventListener("focus", updatePosition);
        updatePosition();
        badge.addEventListener("click", async (e) => {
            e.stopPropagation();
            if (dropdown.style.display === "flex") {
                dropdown.style.display = "none";
                return;
            }
            dropdown.replaceChildren();
            const currentDomain = window.location.hostname.replace("www.", "").toLowerCase();
            const data = await sendToBackground("/logins");
            if (!data?.success) {
                if (data?.error === "Vault locked") {
                    showNote(dropdown, "Vault is locked. Unlock FennecGuard to autofill.");
                }
                else {
                    showNote(dropdown, data?.error || "FennecGuard desktop app is not connected.");
                }
                return;
            }
            const matches = data.items.filter(item => {
                const itemUrl = (item.websiteUrl || item.WebsiteUrl || "").toLowerCase();
                return isDomainMatch(currentDomain, itemUrl);
            });
            if (matches.length === 0) {
                showNote(dropdown, "No saved logins for this domain.");
            }
            else {
                matches.forEach(item => {
                    const itemEl = document.createElement("div");
                    itemEl.className = "item";
                    const title = document.createElement("span");
                    title.className = "item-title";
                    title.textContent = item.title;
                    const user = document.createElement("span");
                    user.className = "item-user";
                    user.textContent = item.username;
                    itemEl.appendChild(title);
                    itemEl.appendChild(user);
                    itemEl.addEventListener("click", async () => {
                        const credData = await sendToBackground("/credential", "POST", { id: item.id });
                        if (credData?.success && credData.credential) {
                            executeFormAutofill(credData.credential.username, credData.credential.password, input);
                        }
                        dropdown.style.display = "none";
                    });
                    dropdown.appendChild(itemEl);
                });
            }
            dropdown.style.display = "flex";
        });
        document.addEventListener("click", () => {
            dropdown.style.display = "none";
        });
    }
    function showNote(dropdown, text) {
        const note = document.createElement("div");
        note.className = "note";
        note.textContent = text;
        dropdown.appendChild(note);
        dropdown.style.display = "flex";
    }
    // ================= Credential Capture =================
    function saveCredentialCandidate() {
        // Read from DOM if buffer missed it
        if (!bufferedPassword || bufferedPassword.length < 4) {
            const pField = document.querySelector('input[type="password"]');
            if (pField && pField.value)
                bufferedPassword = pField.value;
            const uField = findUsernameField(pField);
            if (uField && uField.value)
                bufferedUsername = uField.value.trim();
        }
        if (bufferedPassword && bufferedPassword.length >= 4) {
            const domain = window.location.hostname.replace("www.", "").toLowerCase();
            const username = bufferedUsername || "Account";
            const candidate = {
                domain,
                username,
                password: bufferedPassword,
                originUrl: window.location.href,
                timestamp: Date.now()
            };
            // Store in chrome.storage.local (survives page redirects, subdomains, and worker sleep)
            chrome.storage.local.set({ [STORAGE_KEY]: candidate });
            // For Single-Page Apps (no redirect): check after 1.5 seconds
            setTimeout(() => {
                checkPendingSave();
            }, 1500);
        }
    }
    // Intercept submit, Enter, and button clicks
    document.addEventListener("submit", () => saveCredentialCandidate(), true);
    document.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && e.target instanceof HTMLInputElement) {
            saveCredentialCandidate();
        }
    }, true);
    document.addEventListener("click", (e) => {
        const target = e.target;
        const btn = target.closest("button, input[type='submit'], [role='button']");
        if (btn) {
            const text = (btn.textContent || btn.value || "").toLowerCase();
            if (text.includes("log") || text.includes("sign") || text.includes("submit") || text.includes("continue") || text.includes("next")) {
                saveCredentialCandidate();
            }
        }
    }, true);
    // Capture right before navigation/redirect occurs
    window.addEventListener("beforeunload", () => {
        saveCredentialCandidate();
    }, true);
    // Check storage on page load
    function checkPendingSave() {
        chrome.storage.local.get(STORAGE_KEY, async (result) => {
            const item = result?.[STORAGE_KEY];
            if (!item)
                return;
            // Expire candidates older than 3 minutes
            if (Date.now() - item.timestamp > 180000) {
                chrome.storage.local.remove(STORAGE_KEY);
                return;
            }
            const currentDomain = window.location.hostname.replace("www.", "").toLowerCase();
            // Ensure domain matches candidate
            if (!isDomainMatch(currentDomain, item.domain) && !isDomainMatch(item.domain, currentDomain)) {
                return;
            }
            // Check if credentials are already in vault
            const data = await sendToBackground("/logins");
            if (data?.success && Array.isArray(data.items)) {
                const exists = data.items.some((v) => {
                    const u = (v.username || "").toLowerCase();
                    const w = (v.websiteUrl || "").toLowerCase();
                    return u === item.username.toLowerCase() && isDomainMatch(currentDomain, w);
                });
                if (exists) {
                    chrome.storage.local.remove(STORAGE_KEY);
                    return;
                }
            }
            renderSavePrompt(item.domain, item.username, item.password);
        });
    }
    function renderSavePrompt(domain, username, password) {
        if (document.getElementById("fennecguard-save-prompt-root"))
            return;
        const host = document.createElement("div");
        host.id = "fennecguard-save-prompt-root";
        host.style.position = "fixed";
        host.style.top = "20px";
        host.style.right = "20px";
        host.style.zIndex = "2147483647";
        const targetContainer = document.body || document.documentElement;
        targetContainer.appendChild(host);
        const shadow = host.attachShadow({ mode: "open" });
        const style = document.createElement("style");
        style.textContent = `
      .card {
        width: 330px;
        background: #1e1e1e;
        color: #ffffff;
        border: 1px solid #0067C0;
        border-radius: 12px;
        box-shadow: 0 12px 36px rgba(0,0,0,0.65);
        padding: 16px;
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
        animation: slideIn 0.3s ease-out;
      }
      @keyframes slideIn {
        from { transform: translateY(-30px); opacity: 0; }
        to { transform: translateY(0); opacity: 1; }
      }
      .header { display: flex; align-items: center; gap: 10px; margin-bottom: 12px; }
      .badge { width: 24px; height: 24px; background: #0067C0; border-radius: 50%; display: flex; align-items: center; justify-content: center; }
      .badge img { width: 15px; height: 15px; object-fit: contain; }
      .title { font-weight: 600; font-size: 14px; }
      .details { background: #282828; border-radius: 8px; padding: 10px; margin-bottom: 14px; font-size: 12px; }
      .user { font-weight: 600; color: #ffffff; margin-bottom: 2px; }
      .domain { color: #888888; }
      .actions { display: flex; justify-content: flex-end; gap: 8px; }
      .btn { padding: 7px 16px; border-radius: 6px; font-size: 12px; font-weight: 600; cursor: pointer; border: none; }
      .btn-save { background: #0067C0; color: #ffffff; }
      .btn-save:hover { background: #005299; }
      .btn-cancel { background: transparent; border: 1px solid #383838; color: #ffffff; }
      .btn-cancel:hover { background: #282828; }
    `;
        const card = document.createElement("div");
        card.className = "card";
        const header = document.createElement("div");
        header.className = "header";
        const badge = document.createElement("div");
        badge.className = "badge";
        const img = document.createElement("img");
        img.src = logoUrl;
        badge.appendChild(img);
        const title = document.createElement("div");
        title.className = "title";
        title.textContent = "Save password in FennecGuard?";
        header.appendChild(badge);
        header.appendChild(title);
        const details = document.createElement("div");
        details.className = "details";
        const user = document.createElement("div");
        user.className = "user";
        user.textContent = username;
        const dom = document.createElement("div");
        dom.className = "domain";
        dom.textContent = domain;
        details.appendChild(user);
        details.appendChild(dom);
        const actions = document.createElement("div");
        actions.className = "actions";
        const cancelBtn = document.createElement("button");
        cancelBtn.className = "btn btn-cancel";
        cancelBtn.textContent = "Never";
        cancelBtn.addEventListener("click", () => {
            chrome.storage.local.remove(STORAGE_KEY);
            host.remove();
        });
        const saveBtn = document.createElement("button");
        saveBtn.className = "btn btn-save";
        saveBtn.textContent = "Save";
        saveBtn.addEventListener("click", async () => {
            saveBtn.textContent = "Saving...";
            await sendToBackground("/save", "POST", {
                title: domain.charAt(0).toUpperCase() + domain.slice(1),
                username,
                url: domain,
                password
            });
            chrome.storage.local.remove(STORAGE_KEY);
            host.remove();
        });
        actions.appendChild(cancelBtn);
        actions.appendChild(saveBtn);
        card.appendChild(header);
        card.appendChild(details);
        card.appendChild(actions);
        shadow.appendChild(style);
        shadow.appendChild(card);
    }
    function executeFormAutofill(username, password, originInput) {
        const container = originInput?.form || originInput?.closest("form") || document.body;
        const passField = (originInput && originInput.type === "password")
            ? originInput
            : container.querySelector('input[type="password"]');
        const userField = (originInput && originInput.type !== "password")
            ? originInput
            : findUsernameField(passField || originInput);
        if (userField && username) {
            setNativeInputValue(userField, username);
        }
        if (passField && password) {
            setNativeInputValue(passField, password);
        }
        return !!(userField || passField);
    }
    function findUsernameField(referenceField) {
        const explicitKnown = document.querySelector('input#login_field, input#user_login, input#username, input#email, input[name="login"], input[name="username"], input[name="email"]');
        if (explicitKnown && isSecurelyVisible(explicitKnown))
            return explicitKnown;
        const container = referenceField?.form || referenceField?.closest("form") || document.body;
        const candidates = Array.from(container.querySelectorAll('input[type="text"], input[type="email"]'))
            .filter(isSecurelyVisible);
        if (candidates.length === 0)
            return null;
        const explicit = candidates.find(c => c.autocomplete === "username" || c.autocomplete === "email");
        if (explicit)
            return explicit;
        const heuristic = candidates.find(c => {
            const idName = (c.name + " " + c.id + " " + (c.getAttribute("placeholder") || "")).toLowerCase();
            return idName.includes("user") || idName.includes("email") || idName.includes("login");
        });
        if (heuristic)
            return heuristic;
        return candidates[candidates.length - 1];
    }
    function setNativeInputValue(input, value) {
        input.focus();
        const nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")?.set;
        if (nativeSetter) {
            nativeSetter.call(input, value);
        }
        else {
            input.value = value;
        }
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
        input.dispatchEvent(new Event("blur", { bubbles: true }));
    }
    function isSecurelyVisible(el) {
        if (el.disabled || el.readOnly || el.type === "hidden")
            return false;
        const rect = el.getBoundingClientRect();
        if (rect.width < 12 || rect.height < 12)
            return false;
        const style = window.getComputedStyle(el);
        return style.display !== "none" && style.visibility !== "hidden" && parseFloat(style.opacity) > 0.1;
    }
    function isDomainMatch(targetHost, credentialUrl) {
        if (!targetHost || !credentialUrl)
            return false;
        try {
            let host = credentialUrl;
            if (!host.includes("://"))
                host = "https://" + host;
            const parsedCredHost = new URL(host).hostname.replace("www.", "").toLowerCase();
            if (targetHost === parsedCredHost)
                return true;
            if (targetHost.endsWith("." + parsedCredHost))
                return true;
            return false;
        }
        catch {
            return false;
        }
    }
    function sendToBackground(endpoint, method = "GET", body) {
        return new Promise((resolve) => {
            try {
                chrome.runtime.sendMessage({ target: "API", endpoint, method, body }, (response) => {
                    if (chrome.runtime.lastError) {
                        const errMsg = chrome.runtime.lastError.message || "";
                        if (errMsg.includes("context invalidated")) {
                            resolve({ success: false, error: "Extension updated. Please refresh this page (F5)." });
                        }
                        else {
                            resolve({ success: false, error: "Desktop app not connected." });
                        }
                        return;
                    }
                    resolve(response || { success: false, error: "No response from extension service." });
                });
            }
            catch {
                resolve({ success: false, error: "Extension disconnected." });
            }
        });
    }
})();
