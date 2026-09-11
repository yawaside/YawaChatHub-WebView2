import { useCallback, useEffect, useState } from "react";
import { settingGet, settingSet } from "./bridge";

/** Настройка, которая сохраняется мгновенно — в WebView2 в settings.json, в браузере в localStorage */
export function usePersisted<T>(key: string, fallback: T): [T, (v: T | ((p: T) => T)) => void, boolean] {
  const [value, setValue] = useState<T>(fallback);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    let alive = true;
    void settingGet(key, fallback).then((v) => {
      if (alive) {
        setValue(v);
        setLoaded(true);
      }
    });
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  const set = useCallback(
    (v: T | ((p: T) => T)) => {
      setValue((prev) => {
        const next = typeof v === "function" ? (v as (p: T) => T)(prev) : v;
        void settingSet(key, next);
        return next;
      });
    },
    [key]
  );

  return [value, set, loaded];
}

/** простая строковая настройка */
export function useSetting<T>(key: string, fallback: T): [T, (v: T) => void] {
  const [value, set, loaded] = usePersisted<T>(key, fallback);
  useEffect(() => {
    if (loaded) void settingSet(key, value);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loaded]);
  return [value, set as (v: T) => void];
}
