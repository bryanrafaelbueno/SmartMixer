"use strict";

const HOST_NAME = "com.smartmixer.bridge";
const REFRESH_ALARM = "smartmixer-tab-snapshot";
const rememberedVolumes = new Map();
let nativePort = null;
let reconnectTimer = null;

function connectNative() {
  if (nativePort) return;
  try {
    nativePort = browser.runtime.connectNative(HOST_NAME);
    nativePort.onMessage.addListener(handleHostMessage);
    nativePort.onDisconnect.addListener(() => {
      nativePort = null;
      if (reconnectTimer !== null) clearTimeout(reconnectTimer);
      reconnectTimer = setTimeout(connectNative, 3000);
    });
    void publishTabs();
  } catch (error) {
    console.error("SmartMixer native messaging connection failed:", error);
    if (reconnectTimer !== null) clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(connectNative, 5000);
  }
}

async function readTabValue(tabId, key, fallback) {
  try {
    const value = await browser.sessions.getTabValue(tabId, key);
    return value === undefined || value === null ? fallback : value;
  } catch {
    return fallback;
  }
}

async function writeTabValue(tabId, key, value) {
  try {
    await browser.sessions.setTabValue(tabId, key, value);
  } catch (error) {
    console.error(`SmartMixer could not persist tab value '${key}':`, error);
  }
}

async function setTabVolume(tabId, volume) {
  rememberedVolumes.set(tabId, volume);
  await writeTabValue(tabId, "smartMixerVolume", volume);
  await applyVolume(tabId, volume);
  await publishTabs();
}

async function handleHostMessage(message) {
  if (!message || !Number.isInteger(Number(message.tabId))) return;
  const tabId = Number(message.tabId);
  try {
    if (message.type === "setVolume") {
      const volume = Number(message.volume);
      if (!Number.isFinite(volume)) return;
      await setTabVolume(tabId, Math.max(0, Math.min(1, volume)));
    } else if (message.type === "setMuted" && typeof message.muted === "boolean") {
      await browser.tabs.update(tabId, { muted: message.muted });
      await publishTabs();
    }
  } catch (error) {
    console.error("SmartMixer could not update tab audio:", error);
  }
}

async function applyVolume(tabId, volume) {
  try {
    await browser.scripting.executeScript({
      target: { tabId },
      func: value => {
        for (const media of document.querySelectorAll("audio, video")) {
          media.volume = value;
          if (value > 0 && media.muted) media.muted = false;
        }
      },
      args: [volume],
      world: "MAIN"
    });
  } catch {
    // Browser-internal pages and restricted origins cannot be injected.
  }
}

async function publishTabs() {
  if (!nativePort) return;
  try {
    const tabs = await browser.tabs.query({});
    const snapshot = [];
    for (const tab of tabs) {
      if (typeof tab.id !== "number") continue;
      let smartMixerId = null;
      let openedAt = 0;
      let lastActivatedAt = 0;
      try {
        smartMixerId = await browser.sessions.getTabValue(tab.id, "smartMixerId");
        if (typeof smartMixerId !== "string" || smartMixerId.length === 0) {
          smartMixerId = crypto.randomUUID();
          await browser.sessions.setTabValue(tab.id, "smartMixerId", smartMixerId);
        }
        openedAt = Number(await readTabValue(tab.id, "smartMixerOpenedAt", 0));
        if (!Number.isFinite(openedAt) || openedAt <= 0) {
          openedAt = Date.now();
          await writeTabValue(tab.id, "smartMixerOpenedAt", openedAt);
        }
        lastActivatedAt = Number(await readTabValue(tab.id, "smartMixerLastActivatedAt", 0));
        if (!Number.isFinite(lastActivatedAt) || lastActivatedAt <= 0) {
          lastActivatedAt = tab.active ? Date.now() : openedAt;
          await writeTabValue(tab.id, "smartMixerLastActivatedAt", lastActivatedAt);
        }
      } catch {
        continue;
      }
      let domain = "";
      try {
        domain = new URL(tab.url || "").hostname;
      } catch {
        domain = "";
      }

      let volume = rememberedVolumes.get(tab.id);
      if (volume === undefined) {
        const remembered = Number(await readTabValue(tab.id, "smartMixerVolume", 1));
        volume = Number.isFinite(remembered) ? Math.max(0, Math.min(1, remembered)) : 1;
        rememberedVolumes.set(tab.id, volume);
      }
      try {
        const result = await browser.scripting.executeScript({
          target: { tabId: tab.id },
          func: () => {
            const media = [...document.querySelectorAll("audio, video")];
            return media.length ? media.reduce((sum, item) => sum + item.volume, 0) / media.length : null;
          },
          world: "MAIN"
        });
        if (typeof result?.[0]?.result === "number") volume = result[0].result;
      } catch {
        // Restricted browser pages still appear, but are not remotely controllable.
      }
      snapshot.push({
        id: tab.id,
        smartMixerId,
        title: tab.title || "Untitled tab",
        domain,
        favIconUrl: tab.favIconUrl || null,
        audible: Boolean(tab.audible),
        muted: Boolean(tab.mutedInfo?.muted),
        volume,
        openedAt,
        lastActivatedAt,
        active: Boolean(tab.active)
      });
    }
    nativePort.postMessage({ type: "tabs", tabs: snapshot });
  } catch (error) {
    console.error("SmartMixer could not enumerate tabs:", error);
  }
}

browser.alarms.create(REFRESH_ALARM, { periodInMinutes: 0.1 });
browser.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === REFRESH_ALARM) void publishTabs();
});
browser.tabs.onCreated.addListener(() => void publishTabs());
browser.tabs.onRemoved.addListener(tabId => {
  rememberedVolumes.delete(tabId);
  void publishTabs();
});
browser.tabs.onUpdated.addListener((_tabId, changeInfo) => {
  if (changeInfo.status === "complete") {
    void readTabValue(_tabId, "smartMixerVolume", null).then(volume => {
      if (typeof volume === "number" && Number.isFinite(volume))
        void applyVolume(_tabId, Math.max(0, Math.min(1, volume)));
    });
  }
  if (changeInfo.status === "complete" || changeInfo.audible !== undefined || changeInfo.mutedInfo)
    void publishTabs();
});
browser.tabs.onActivated.addListener(({ tabId }) => {
  void writeTabValue(tabId, "smartMixerLastActivatedAt", Date.now()).then(() => publishTabs());
});

connectNative();
