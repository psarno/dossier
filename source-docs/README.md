# Source Documents

This directory contains the **three markdown source documents** that feed the dossier pipeline.

The parser has strict expectations about structure. Follow the conventions below so sections, entries, and tags parse correctly.

## Document 1: Summary Document (`summary.md`)

**Purpose**: Primary-source claims organized by theme or narrative arc.

**Structure**:
- File can be named anything (e.g., `summary.md`, `claims.md`, `record.md`)
- Each top-level section becomes a "section" in the database
- Sections are tagged with evidentiary weight

**Heading Conventions**:

```markdown
## Section Title

Body text explaining the claim or theme. Include quotes from sources.

**Source:** <URL or citation text>

More body text with another claim.

`[CONFIRMED]` This is a direct quote or paraphrase from a primary document.

**Source:** <URL>

*[Context: Optional explanatory note about why this matters or what it relates to]*

## Another Section

...
```

**Tag Syntax**:
- Tags appear inline, wrapped in backticks: `` `[CONFIRMED]` ``
- Available tags:
  - `[CONFIRMED]` — In primary documents, court records, filings. Directly verifiable.
  - `[CORROBORATED]` — Credible per investigative review across multiple sources.
  - `[DOCUMENTED CLAIM]` — In official documents but not independently verified.
  - `[CONFIRMED GOVT ACTION]` — A government action confirmed, motive unknown.
  - `[ANOMALOUS]` — Confirmed fact that is procedurally or legally extraordinary.

**Source Citations**:
- Lines starting with `**Source:**` are extracted as citations
- Multiple sources can be listed; the parser splits on semicolons
- Context (section heading, surrounding claim) is recorded for each source

**Optional Context Blocks**:
- `*[Context: explanation]* ` at the end of a paragraph adds an analysis note
- Rendered as a styled block labeled "Analysis"

## Document 2: Names Index (`names.md`)

**Purpose**: People, organizations, or entities mentioned in the research, ranked by prominence.

**Structure**:
- Tier 1 (primary subjects) listed first under `## Tier 1`
- Tier 2 (secondary figures) under `## Tier 2`
- The heading text is the boundary detector — name order matters

**Heading Conventions**:

```markdown
## Tier 1

### Subject Name (relationship or role)

Brief description of who they are and why they appear in the record.
Mention key facts, connections, dates.

**Source:** <URL or citation>

### Another Tier 1 Figure

...

## Tier 2

### Secondary Figure

...
```

**Entry Format**:
- Each `###` heading is parsed as an entry
- The heading text before the first `(` is the name
- Text in `(...)` is the role/relationship descriptor (optional but recommended)
- Body is a short description (1–3 sentences)

**Tier Boundary**:
- The parser looks for `## Tier 2` heading text
- Entries **above** that heading are Tier 1
- Entries **below** are Tier 2
- If no explicit `## Tier 2` heading, all entries are Tier 1

## Document 3: Analytical Framework (`framework.md`)

**Purpose**: Cross-cutting themes, patterns, or analytical structure applied to the subject.

**Heading Conventions**:

```markdown
# Document Title

## Theme or Analytical Heading

Body text describing the theme, pattern, or framework element.

`[CONFIRMED]` Claim with tag.

**Source:** <URL>

## Another Theme

...
```

**Requirements**:
- File must start with `# Title` (single `#`)
- Sections are `##` headings
- Tags, sources, and context blocks work the same as in the summary

## Tips

1. **Consistent Markdown**: Use standard Markdown (ATX headings `#`, `##`, etc.; inline `**bold**` and `*italic*`).
2. **Citation Quality**: Include URLs or full citations so readers can verify claims. If a source is behind a paywall, note that.
3. **Tag Placement**: Tags should appear near the specific claim they describe, not in section headers.
4. **Section Length**: Sections with fewer than ~50 characters or entries with fewer than ~20 characters may be filtered by the pipeline. Set `PIPELINE_MIN_SECTIONS` and `PIPELINE_MIN_ENTRIES` in the API environment to adjust thresholds.
5. **AI Enrichment**: The API calls Claude to generate slugs, summaries, and suggested tags for each section. Ensure section content is substantive enough for the AI to work with.

## Example

**summary.md**:
```markdown
## Timeline of Key Events

`[CONFIRMED]` The subject founded Organization X in 2010, per the organization's official 501(c)(3) filing.

**Source:** Organization X IRS Form 990, 2010

`[CORROBORATED]` Early funding came from three venture firms, confirmed by multiple news reports and the subject's own LinkedIn profile.

**Source:** TechCrunch article, Dec 2010; Wall Street Journal, Jan 2011

*[Context: This funding round was unusually quick — normally these firms take 6+ months to commit]*

## Policy Shift in 2015

`[DOCUMENTED CLAIM]` The subject claimed in an interview that the organization shifted its policy in 2015.

**Source:** Interview transcript, May 2015

`[ANOMALOUS]` The shift occurred two weeks before a regulatory change that made the old policy illegal.

**Source:** Federal Register notice, May 15, 2015
```

**names.md**:
```markdown
## Tier 1

### Subject Name (Founder)

Founder of Organization X. Central figure in the research timeline.

**Source:** Organization website

## Tier 2

### Co-founder (Early employee)

Co-founder who left in 2012.

**Source:** LinkedIn profile
```

**framework.md**:
```markdown
# Organizational Structure and Timeline

## Growth Phase (2010–2014)

The organization went through rapid expansion in this period.

`[CONFIRMED]` The team grew from 3 to 50 people.

**Source:** Official press release, 2014
```

---

**When you're ready**: Replace these three files with your own markdown sources and run the pipeline via `/api/admin/upload`.
