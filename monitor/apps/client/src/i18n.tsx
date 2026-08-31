import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import en from "./locales/en.json";
import uk from "./locales/uk.json";

export type Locale = "en" | "uk";
type Params = Record<string, string | number>;
type Dictionary = typeof en;

const dictionaries: Record<Locale, Dictionary> = { en, uk };
const browserLocale: Locale = typeof navigator !== "undefined" && navigator.languages.some(language => language.toLowerCase().startsWith("uk")) ? "uk" : "en";

function lookup(dictionary: Dictionary, key: string): string | undefined {
  let value: unknown = dictionary;
  for (const segment of key.split(".")) {
    if (!value || typeof value !== "object" || !(segment in value)) return undefined;
    value = (value as Record<string, unknown>)[segment];
  }
  return typeof value === "string" ? value : undefined;
}

function interpolate(value: string, params?: Params): string {
  return value.replace(/\{\{(\w+)\}\}/g, (_, key: string) => String(params?.[key] ?? `{{${key}}}`));
}

interface I18nValue { locale: Locale; setLocale: (locale: Locale) => void; t: (key: string, params?: Params) => string; }
const I18nContext = createContext<I18nValue | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocale] = useState<Locale>(() => {
    const savedLocale = typeof localStorage !== "undefined" ? localStorage.getItem("sarmat-locale") : null;
    return savedLocale === "uk" || savedLocale === "en" ? savedLocale : browserLocale;
  });
  const t = (key: string, params?: Params) => interpolate(lookup(dictionaries[locale], key) ?? lookup(en, key) ?? key, params);
  useEffect(() => { localStorage.setItem("sarmat-locale", locale); document.documentElement.lang = locale; document.title = `${t("app.name")} — ${t("app.subtitle")}`; }, [locale]);
  return <I18nContext.Provider value={{ locale, setLocale, t }}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const value = useContext(I18nContext);
  if (!value) throw new Error("I18nProvider missing");
  return value;
}
