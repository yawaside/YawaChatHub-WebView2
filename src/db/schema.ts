import { jsonb, pgTable, text, timestamp } from "drizzle-orm/pg-core";

/** Единственная строка настроек приложения (settings.json на сервере). */
export const appSettings = pgTable("app_settings", {
  id: text("id").primaryKey(),
  data: jsonb("data").$type<Record<string, unknown>>().notNull(),
  updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull(),
});
