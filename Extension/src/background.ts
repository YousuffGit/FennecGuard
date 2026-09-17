(() => {
  const BG_API_BASE = "http://127.0.0.1:41893";
  let authToken = "";

  async function getAuthToken(): Promise<string> {
    if (authToken) return authToken;
    try {
      const res = await fetch(chrome.runtime.getURL("token.json"));
      const data = await res.json();
      authToken = data.token || "";
    } catch {}
    return authToken;
  }

  chrome.runtime.onMessage.addListener((request, _sender, sendResponse) => {
    // API Loopback Proxy
    if (request.target === "API") {
      (async () => {
        // Fallback checks to prevent any "undefined" URL string parsing
        const endpoint = request.endpoint || request.url || (request.payload ? request.payload.endpoint : null);
        if (!endpoint || typeof endpoint !== "string") {
          sendResponse({ success: false, error: "Invalid API endpoint" });
          return;
        }

        const token = await getAuthToken();
        const cleanEndpoint = endpoint.startsWith("/") ? endpoint : `/${endpoint}`;

        try {
          const res = await fetch(`${BG_API_BASE}${cleanEndpoint}`, {
            method: request.method || "GET",
            headers: {
              "Content-Type": "application/json",
              "X-FennecGuard-Auth": token
            },
            body: request.body ? JSON.stringify(request.body) : undefined
          });

          if (!res.ok) {
            sendResponse({ success: false, error: `Server returned ${res.status}` });
            return;
          }

          const data = await res.json();
          sendResponse(data);
        } catch (err: any) {
          sendResponse({ success: false, error: err?.message || "Desktop app offline" });
        }
      })();

      return true; // Keep channel open for async response
    }

    if (request.target === "STAGE_PENDING_SAVE") {
      if (chrome.storage?.session) {
        chrome.storage.session.set({ pendingSave: { ...request.credential, timestamp: Date.now() } });
      }
      sendResponse({ success: true });
      return false;
    }

    if (request.target === "GET_PENDING_SAVE") {
      if (chrome.storage?.session) {
        chrome.storage.session.get("pendingSave", (result) => {
          const item = result?.pendingSave;
          if (item && item.domain === request.domain && (Date.now() - item.timestamp < 180000)) {
            sendResponse({ pending: item });
          } else {
            sendResponse({ pending: null });
          }
        });
        return true;
      }
      sendResponse({ pending: null });
      return false;
    }

    if (request.target === "CLEAR_PENDING_SAVE") {
      if (chrome.storage?.session) {
        chrome.storage.session.remove("pendingSave");
      }
      sendResponse({ success: true });
      return false;
    }
  });
})();
