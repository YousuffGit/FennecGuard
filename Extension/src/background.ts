// FennecGuard Service Worker: Native Messaging Bridge
const NATIVE_HOST = "com.fennecguard.nativehost";

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.target === "NATIVE_HOST") {
    chrome.runtime.sendNativeMessage(NATIVE_HOST, message.payload, (response) => {
      if (chrome.runtime.lastError) {
        sendResponse({ success: false, error: chrome.runtime.lastError.message });
      } else {
        sendResponse(response);
      }
    });
    return true; // Keep message channel open for async response
  }
});
