(() => {
  interface VaultLogin {
    id: string;
    title: string;
    username: string;
    websiteUrl: string;
  }

  const POPUP_API_BASE = "http://127.0.0.1:41893";
  let cachedToken = "";

  async function getAuthToken(): Promise<string> {
    if (cachedToken) return cachedToken;
    try {
      const res = await fetch(chrome.runtime.getURL("token.json"));
      const data = await res.json();
      cachedToken = data.token || "";
    } catch {}
    return cachedToken;
  }

  document.addEventListener("DOMContentLoaded", async () => {
    const themeBtn = document.getElementById("theme-toggle-btn") as HTMLButtonElement;
    const lockBtn = document.getElementById("lock-btn") as HTMLButtonElement;
    const lockedView = document.getElementById("locked-view") as HTMLElement;
    const unlockedView = document.getElementById("unlocked-view") as HTMLElement;
    const disconnectedView = document.getElementById("disconnected-view") as HTMLElement;
    const disconnectedReason = document.getElementById("disconnected-reason") as HTMLElement;
    const masterPasswordInput = document.getElementById("master-password-input") as HTMLInputElement;
    const unlockBtn = document.getElementById("unlock-btn") as HTMLButtonElement;
    const unlockError = document.getElementById("unlock-error") as HTMLElement;
    const searchInput = document.getElementById("search-input") as HTMLInputElement;
    const credentialsList = document.getElementById("credentials-list") as HTMLElement;
    const siteDomainText = document.getElementById("site-domain") as HTMLElement;
    const retryBtn = document.getElementById("retry-btn") as HTMLButtonElement;

    const savedTheme = localStorage.getItem("fennecguard_ext_theme") || "dark";
    document.body.setAttribute("data-theme", savedTheme);

    themeBtn.addEventListener("click", () => {
      const current = document.body.getAttribute("data-theme");
      const next = current === "light" ? "dark" : "light";
      document.body.setAttribute("data-theme", next);
      localStorage.setItem("fennecguard_ext_theme", next);
    });

    const activeTab = await getActiveTab();
    let currentHostname = "";
    if (activeTab?.url) {
      try {
        const url = new URL(activeTab.url);
        currentHostname = url.hostname.replace("www.", "").toLowerCase();
        siteDomainText.textContent = currentHostname;
      } catch {
        siteDomainText.textContent = "Unavailable";
      }
    }

    let vaultItems: VaultLogin[] = [];

    async function checkStatus(): Promise<void> {
      const res = await popupApiCall("/status");
      if (!res || !res.success) {
        if (disconnectedReason) {
          if (res?.error === "Unauthorized token") {
            disconnectedReason.textContent = "Token mismatch: Click 'Reload' on FennecGuard in chrome://extensions";
          } else {
            disconnectedReason.textContent = "FennecGuard desktop client is closed or not responding.";
          }
        }
        showView("disconnected");
        return;
      }

      if (res.isUnlocked) {
        showView("unlocked");
        await loadLogins();
      } else {
        showView("locked");
      }
    }

    function showView(view: "locked" | "unlocked" | "disconnected"): void {
      lockedView.style.display = view === "locked" ? "flex" : "none";
      unlockedView.style.display = view === "unlocked" ? "flex" : "none";
      disconnectedView.style.display = view === "disconnected" ? "flex" : "none";
      lockBtn.style.display = view === "unlocked" ? "block" : "none";
      unlockError.textContent = "";
    }

    async function loadLogins(): Promise<void> {
      const res = await popupApiCall("/logins");
      if (res?.success && Array.isArray(res.items)) {
        vaultItems = res.items;
        renderLogins(searchInput.value.trim());
      }
    }

    function isSecureDomainMatch(targetHost: string, credentialUrl: string): boolean {
      if (!targetHost || !credentialUrl) return false;
      try {
        let host = credentialUrl;
        if (!host.includes("://")) host = "https://" + host;
        const parsedCredHost = new URL(host).hostname.replace("www.", "").toLowerCase();

        if (targetHost === parsedCredHost) return true;
        if (targetHost.endsWith("." + parsedCredHost)) return true;
        return false;
      } catch {
        return false;
      }
    }

    function renderLogins(filter: string = ""): void {
      credentialsList.replaceChildren();

      const matches = vaultItems.filter(item => {
        const q = filter.toLowerCase();
        const itemUrl = (item.websiteUrl || (item as any).WebsiteUrl || "").toLowerCase();
        const matchesText = item.title.toLowerCase().includes(q) ||
                            item.username.toLowerCase().includes(q) ||
                            itemUrl.includes(q);

        if (filter) return matchesText;
        return isSecureDomainMatch(currentHostname, itemUrl);
      });

      if (matches.length === 0) {
        const empty = document.createElement("div");
        empty.className = "empty-state";
        empty.textContent = filter ? "No matching logins found" : "No credentials saved for this domain";
        credentialsList.appendChild(empty);
        return;
      }

      matches.forEach(item => {
        const card = document.createElement("div");
        card.className = "credential-card";

        const info = document.createElement("div");
        info.className = "credential-info";

        const title = document.createElement("span");
        title.className = "credential-title";
        title.textContent = item.title;

        const user = document.createElement("span");
        user.className = "credential-user";
        user.textContent = item.username;

        info.appendChild(title);
        info.appendChild(user);

        const actions = document.createElement("div");
        actions.className = "credential-actions";

        const copyBtn = document.createElement("button");
        copyBtn.className = "action-btn copy-btn";
        copyBtn.textContent = "Copy";
        copyBtn.addEventListener("click", async () => {
          const credRes = await popupApiCall("/credential", "POST", { id: item.id });
          if (credRes?.success && credRes.credential?.password) {
            navigator.clipboard.writeText(credRes.credential.password);
            copyBtn.textContent = "Copied!";
            setTimeout(() => { copyBtn.textContent = "Copy"; }, 1500);
          }
        });

        const fillBtn = document.createElement("button");
        fillBtn.className = "action-btn fill-btn";
        fillBtn.textContent = "Autofill";
        fillBtn.addEventListener("click", async () => {
          const credRes = await popupApiCall("/credential", "POST", { id: item.id });
          if (credRes?.success && credRes.credential && activeTab?.id) {
            chrome.tabs.sendMessage(activeTab.id, {
              action: "AUTOFILL_FORM",
              username: credRes.credential.username,
              password: credRes.credential.password
            }, (response) => {
              if (chrome.runtime.lastError) {
                fillBtn.textContent = "Refresh Page";
                setTimeout(() => { fillBtn.textContent = "Autofill"; }, 2000);
                return;
              }
              if (response?.success) {
                fillBtn.textContent = "Filled!";
                setTimeout(() => { fillBtn.textContent = "Autofill"; }, 1500);
              } else {
                fillBtn.textContent = "No Form";
                setTimeout(() => { fillBtn.textContent = "Autofill"; }, 1500);
              }
            });
          }
        });

        const deleteBtn = document.createElement("button");
        deleteBtn.className = "action-btn delete-btn";
        deleteBtn.title = "Delete login";
        deleteBtn.textContent = "✕";
        deleteBtn.addEventListener("click", async () => {
          if (confirm(`Are you sure you want to permanently delete '${item.title}' (${item.username})?`)) {
            const delRes = await popupApiCall("/delete", "POST", { id: item.id });
            if (delRes?.success) {
              await loadLogins();
            }
          }
        });

        actions.appendChild(copyBtn);
        actions.appendChild(fillBtn);
        actions.appendChild(deleteBtn);

        card.appendChild(info);
        card.appendChild(actions);

        credentialsList.appendChild(card);
      });
    }

    unlockBtn.addEventListener("click", async () => {
      const password = masterPasswordInput.value;
      if (!password) return;

      unlockBtn.disabled = true;
      unlockBtn.textContent = "Unlocking...";

      const res = await popupApiCall("/unlock", "POST", { password });
      unlockBtn.disabled = false;
      unlockBtn.textContent = "Unlock Vault";

      if (res?.success) {
        masterPasswordInput.value = "";
        showView("unlocked");
        await loadLogins();
      } else {
        unlockError.textContent = res?.error || "Incorrect master password.";
      }
    });

    masterPasswordInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter") unlockBtn.click();
    });

    lockBtn.addEventListener("click", async () => {
      await popupApiCall("/lock", "POST");
      showView("locked");
    });

    searchInput.addEventListener("input", () => {
      renderLogins(searchInput.value.trim());
    });

    retryBtn.addEventListener("click", checkStatus);

    await checkStatus();
  });

  async function popupApiCall(endpoint: string, method: string = "GET", body?: any): Promise<any> {
    try {
      const token = await getAuthToken();
      const res = await fetch(`${POPUP_API_BASE}${endpoint}`, {
        method,
        headers: {
          "Content-Type": "application/json",
          "X-FennecGuard-Auth": token
        },
        body: body ? JSON.stringify(body) : undefined
      });
      return await res.json();
    } catch {
      return { success: false, error: "Disconnected" };
    }
  }

  async function getActiveTab(): Promise<chrome.tabs.Tab | undefined> {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    return tabs[0];
  }
})();
