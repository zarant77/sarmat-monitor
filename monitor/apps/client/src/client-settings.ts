export const historyEventCategories = ["measurement", "charge", "discharge", "manual", "transfer"] as const;
export type HistoryEventCategory = typeof historyEventCategories[number];

export interface HistoryFilterSettings {
  categories: HistoryEventCategory[];
  from: string;
  to: string;
}

interface StoredClientSettings {
  crewId?: string;
  batteryHistoryFilter?: Partial<HistoryFilterSettings>;
}

export const defaultHistoryFilter: HistoryFilterSettings = {
  categories: [...historyEventCategories],
  from: "",
  to: ""
};

class ClientSettingsStore {
  private readonly storageKey = "sbm-client-settings:v1";

  private read(): StoredClientSettings {
    if (typeof localStorage === "undefined") return {};
    try {
      const value = JSON.parse(localStorage.getItem(this.storageKey) ?? "{}");
      return value && typeof value === "object" ? value as StoredClientSettings : {};
    } catch {
      return {};
    }
  }

  private write(settings: StoredClientSettings) {
    if (typeof localStorage === "undefined") return;
    try { localStorage.setItem(this.storageKey, JSON.stringify(settings)); } catch { /* Storage can be unavailable in private browser modes. */ }
  }

  getCrewId() {
    const settings = this.read();
    if (settings.crewId) return settings.crewId;
    if (typeof localStorage !== "undefined") {
      const legacy = localStorage.getItem("sbm-crew");
      if (legacy) { this.setCrewId(legacy); return legacy; }
    }
    return "all";
  }

  setCrewId(crewId: string) {
    this.write({ ...this.read(), crewId });
  }

  getHistoryFilter(): HistoryFilterSettings {
    const value = this.read().batteryHistoryFilter;
    const categories = Array.isArray(value?.categories)
      ? value.categories.filter((category): category is HistoryEventCategory => historyEventCategories.includes(category as HistoryEventCategory))
      : defaultHistoryFilter.categories;
    return {
      categories,
      from: typeof value?.from === "string" ? value.from : "",
      to: typeof value?.to === "string" ? value.to : ""
    };
  }

  setHistoryFilter(filter: HistoryFilterSettings) {
    this.write({ ...this.read(), batteryHistoryFilter: filter });
  }
}

export const clientSettings = new ClientSettingsStore();
