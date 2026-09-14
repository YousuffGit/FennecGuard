"use strict";
// FennecGuard Content Script: Form Scraper & Autofill (Handles all frames)
chrome.runtime.onMessage.addListener((request, _sender, sendResponse) => {
    if (request.action === "AUTOFILL_FORM") {
        const { username, password } = request;
        const filled = fillCredentials(username, password);
        sendResponse({ success: filled });
    }
});
function fillCredentials(username, password) {
    const passwordInputs = Array.from(document.querySelectorAll('input[type="password"]'));
    if (passwordInputs.length === 0)
        return false;
    const targetPassword = passwordInputs[0];
    targetPassword.value = password;
    targetPassword.dispatchEvent(new Event("input", { bubbles: true }));
    targetPassword.dispatchEvent(new Event("change", { bubbles: true }));
    // Find corresponding username/email input
    const allInputs = Array.from(document.querySelectorAll('input[type="text"], input[type="email"]'));
    const targetUsername = allInputs.find(input => {
        const name = (input.name || input.id || "").toLowerCase();
        return name.includes("user") || name.includes("email") || name.includes("login");
    }) || allInputs[0];
    if (targetUsername) {
        targetUsername.value = username;
        targetUsername.dispatchEvent(new Event("input", { bubbles: true }));
        targetUsername.dispatchEvent(new Event("change", { bubbles: true }));
    }
    return true;
}
