export interface Entry {
  id: number;
  slug: string;
  name: string;
  tier: number;
  description: string;
  sectionRefs: string; // JSON array string
  docVersion: string;
}

export interface EntrySummary {
  id: number;
  slug: string;
  name: string;
  tier: number;
  sectionRefs: string;
}
