(() => {
  const BG_API_BASE = "http://127.0.0.1:41893";

  chrome.runtime.onMessage.addListener((request, _sender, sendResponse) => {
    // API Loopback Proxy
    if (request.target === "API") {
      fetch(`${BG_API_BASE}${request.endpoint}`, {
        method: request.method || "GET",
        headers: {
          "Content-Type": "application/json",
          "X-FennecGuard-Client": "Extension"
        },
        body: request.body ? JSON.stringify(request.body) : undefined
      })
        .then(res => res.json())
        .then(data => sendResponse(data))
        .catch(err => sendResponse({ success: false, error: err?.message || "Desktop app offline" }));

      return true;
    }

    // Persist pending save across service worker restarts
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
