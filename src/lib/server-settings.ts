// ─── Хранение настроек на сервере (PostgreSQL, аналог SettingsService.cs) ───
import { db } from "@/db";
import { appSettings } from "@/db/schema";
import { DEFAULT_SETTINGS, type Settings } from "./types";
import { deepMerge, token as genToken } from "./utils";
import { publish } from "./bus";

const ROW_ID = "global";

async function load(): Promise<{ fresh: boolean; settings: Settings }> {
  const rows = await db.select().from(appSettings).limit(1);
  if (rows.length === 0) {
    const fresh: Settings = { ...DEFAULT_SETTINGS, token: genToken() };
    await db.insert(appSettings).values({ id: ROW_ID, data: fresh as unknown as Record<string, unknown> });
    return { fresh: true, settings: fresh };
  }
  // миграция схемы: дефолты применяются к новым полям (ТЗ §11)
  const merged = deepMerge(DEFAULT_SETTINGS, rows[0].data) as Settings;
  if ((rows[0].data as { settingsSchemaVersion?: number }).settingsSchemaVersion !== undefined) {
    const v = (rows[0].data as { settingsSchemaVersion?: number }).settingsSchemaVersion ?? 0;
    // при схеме < 3 closeToTray обязательно false (ТЗ §11)
    if (v < 3) merged.closeToTray = false;
    merged.settingsSchemaVersion = DEFAULT_SETTINGS.settingsSchemaVersion;
  }
  if (!merged.token || merged.token === "yawa_demo") merged.token = genToken();
  return { fresh: false, settings: merged };
}

export async function getSettings(): Promise<Settings> {
  try {
    return (await load()).settings;
  } catch (e) {
    console.error("settings load failed, using defaults:", e);
    return { ...DEFAULT_SETTINGS, token: genToken() };
  }
}

export async function patchSettings(patch: Record<string, unknown>): Promise<Settings> {
  const current = await getSettings();
  const merged = deepMerge(current, patch) as Settings;
  merged.token = current.token; // токен не перезаписываем
  try {
    await db
      .insert(appSettings)
      .values({ id: ROW_ID, data: merged as unknown as Record<string, unknown>, updatedAt: new Date() })
      .onConflictDoUpdate({ target: appSettings.id, set: { data: merged as unknown as Record<string, unknown>, updatedAt: new Date() } });
  } catch (e) {
    console.error("settings save failed:", e);
  }
  // изменения мгновенно отражаются в OBS и оверлее (ТЗ: критерий приёмки №2)
  publish({ type: "widget:config", cfg: merged.widget, look: lookOf(merged) }, ["widget"]);
  publish({ type: "overlay:config", cfg: merged.overlay }, ["overlay"]);
  return merged;
}

/** Параметры оформления виджета под CSS-переменные (ТЗ §12.4). */
export function lookOf(s: Settings) {
  const w = s.widget;
  return {
    fontSize: w.fontSize,
    radius: w.radius,
    bgOpacity: w.bgOpacity,
    shadow: w.shadow,
    border: w.border,
    style: w.style,
    theme: w.theme,
    textColor: w.textColor,
    nameColor: w.nameColor,
    bgColor: w.bgColor,
    bgImage: w.bgImage,
  };
}
