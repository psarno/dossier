export interface Section {
  id: number;
  slug: string;
  title: string;
  body: string;
  docType: string;
  docVersion: string;
  sortOrder: number;
  tagsPresent: string; // JSON array string
}

export interface SectionSummary {
  id: number;
  slug: string;
  title: string;
  docType: string;
  docVersion: string;
  sortOrder: number;
  tagsPresent: string;
}
