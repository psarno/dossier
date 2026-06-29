export interface ResearchConfig {
  subject: string;
  branding: BrandingConfig;
  centralNode: CentralNodeConfig;
  documents: DocumentConfig[];
  tags: TagConfig[];
  sourceCitation: SourceCitationConfig;
}

export interface BrandingConfig {
  siteTitle: string;
  navBrand: string;
  tagline: string;
  contactEmail: string;
  domain: string;
}

export interface CentralNodeConfig {
  id: string;
  aliases: string[];
}

export interface DocumentConfig {
  type: string;
  label: string;
  route: string;
}

export interface TagConfig {
  key: string;
  label: string;
  description: string;
}

export interface SourceCitationConfig {
  enabled: boolean;
  label: string;
}
