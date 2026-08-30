import { drizzle, type NodePgDatabase } from "drizzle-orm/node-postgres";
import { Pool } from "pg";

// ВАЖНО: подключение к БД инициализируется ЛЕНИВО.
// Раньше модуль бросал `Error: DATABASE_URL is required` прямо при импорте,
// из-за чего `next build` падал в CI на этапе «Collecting page data»
// (GitHub Actions собирает проект без базы и без переменных окружения).
// Теперь пул создаётся только при первом реальном запросе к БД,
// а сборка проходит без DATABASE_URL.

const globalForDb = globalThis as typeof globalThis & {
  __arenaNextJsPostgresqlPool?: Pool;
  __arenaNextJsPostgresqlDb?: NodePgDatabase;
};

function getPool(): Pool {
  if (!globalForDb.__arenaNextJsPostgresqlPool) {
    const databaseUrl = process.env.DATABASE_URL;
    if (!databaseUrl) {
      throw new Error("DATABASE_URL is required (задайте переменную окружения при запуске сервера)");
    }
    globalForDb.__arenaNextJsPostgresqlPool = new Pool({ connectionString: databaseUrl });
  }
  return globalForDb.__arenaNextJsPostgresqlPool;
}

function getDb(): NodePgDatabase {
  if (!globalForDb.__arenaNextJsPostgresqlDb) {
    globalForDb.__arenaNextJsPostgresqlDb = drizzle(getPool());
  }
  return globalForDb.__arenaNextJsPostgresqlDb;
}

/** Ленивый прокси: обращение к любому методу создаёт подключение при первом вызове. */
export const db: NodePgDatabase = new Proxy({} as NodePgDatabase, {
  get(_target, prop, receiver) {
    const real = getDb() as unknown as Record<string | symbol, unknown>;
    const value = Reflect.get(real, prop, receiver);
    return typeof value === "function" ? (value as (...a: unknown[]) => unknown).bind(real) : value;
  },
});

export const pool: Pool = new Proxy({} as Pool, {
  get(_target, prop, receiver) {
    const real = getPool() as unknown as Record<string | symbol, unknown>;
    const value = Reflect.get(real, prop, receiver);
    return typeof value === "function" ? (value as (...a: unknown[]) => unknown).bind(real) : value;
  },
});
