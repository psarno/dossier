export interface GeneratedSourcesSummary {
  generatedAt: string;
  summaryVersion: string;
  namesVersion: string;
  totals: SourceTotals;
  groups: GeneratedSourcesGroupSummary[];
}

export interface GeneratedSourcesGroupSummary {
  key: string;
  label: string;
  itemCount: number;
}

export interface GeneratedSourcesGroup extends GeneratedSourcesGroupSummary {
  items: GeneratedSourceItem[];
}

export interface SourceTotals {
  totalFragments: number;
  parsedItems: number;
  ambiguousItems: number;
}

export interface GeneratedSourceItem {
  raw: string;
  rawVariants: string[];
  normalizedLabel: string;
  type: string;
  author?: string | null;
  outlet?: string | null;
  account?: string | null;
  platform?: string | null;
  dateText?: string | null;
  notes?: string | null;
  sourceDocuments: string[];
  occurrences: SourceOccurrence[];
  confidence: string;
  needsReview: boolean;
}

export interface SourceOccurrence {
  document: string;
  lineNumber: number;
  contextLabel: string;
}

export interface SourceReviewItem {
  raw: string;
  document: string;
  lineNumber: number;
  contextLabel: string;
  reason: string;
}
