export interface SearchResult {
  resultType: 'section' | 'entry';
  slug: string;
  title: string;
  snippet: string;
  rank: number;
}
