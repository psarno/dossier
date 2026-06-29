import { Pipe, PipeTransform, inject, SecurityContext } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked } from 'marked';

// Exact-match tag definitions for specific tags: [TAG TEXT] → [css-class, display label]
// Order matters: more specific CONFIRMED variants must come before the generic prefix rule.
const TAG_MAP: Record<string, [string, string]> = {
  'CONFIRMED GOVT ACTION': ['tag-govt-action', 'Confirmed Govt Action'],
  'CORROBORATED': ['tag-corroborated', 'Corroborated'],
  'DOCUMENTED CLAIM': ['tag-documented-claim', 'Documented Claim'],
  'ANOMALOUS': ['tag-anomalous', 'Anomalous'],
};

@Pipe({ name: 'safeHtml', standalone: true })
export class SafeHtmlPipe implements PipeTransform {
  private sanitizer = inject(DomSanitizer);

  transform(markdown: string | null | undefined, mode: 'block' | 'inline' = 'block'): SafeHtml {
    if (!markdown) return '';
    // Ensure bold labels at start of lines become their own paragraphs
    // (names entries use single \n between **Label:** lines; markdown needs \n\n)
    const normalized = markdown.replace(/\n(\*\*)/g, '\n\n$1');
    const html = mode === 'inline'
      ? marked.parseInline(normalized) as string
      : marked.parse(normalized) as string;
    const processed = postProcess(html);
    // The content originates from our own API/pipeline — bypassing is intentional
    return this.sanitizer.bypassSecurityTrustHtml(processed);
  }
}

function postProcess(html: string): string {
  let result = html;

  // Split compound <code>[TAG1][TAG2]</code> into individual <code>[TAG1]</code><code>[TAG2]</code>
  // (source markdown uses a single backtick span for adjacent tags)
  result = result.replace(/<code>(\[[^\]]+\]){2,}<\/code>/g, (match) => {
    const inner = match.slice('<code>'.length, -'</code>'.length);
    return inner.replace(/\[[^\]]+\]/g, tag => `<code>${tag}</code>`);
  });

  // Convert <code>[TAG]</code> → styled badge spans
  for (const [tagText, [cssClass, label]] of Object.entries(TAG_MAP)) {
    // Escape special regex chars in tagText
    const escaped = tagText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const regex = new RegExp(`<code>\\[${escaped}\\]<\\/code>`, 'gi');
    result = result.replace(regex, `<span class="tag ${cssClass}">${label}</span>`);
  }

  // CONFIRMED prefix rule: any [CONFIRMED ...] variant not already matched → tag-confirmed (green).
  // This covers [CONFIRMED — PRIMARY], [CONFIRMED via secondary], one-off source citations, etc.
  // without requiring the TAG_MAP to enumerate every variant.
  result = result.replace(/<code>\[(CONFIRMED[^\]]*)\]<\/code>/gi,
    (_match, label) => `<span class="tag tag-confirmed">${label}</span>`
  );

  // Catch-all: any remaining <code>[LABEL]</code> not matched above → neutral status badge
  result = result.replace(/<code>\[([^\]]+)\]<\/code>/gi,
    (_match, label) => `<span class="tag tag-status">${label}</span>`
  );

  // Convert *[Context: ...] and *[Why it matters: ...]* italic blocks to analysis blocks
  // marked renders *[Context: text]* as <em>[Context: text]</em>
  result = result.replace(
    /<em>\[(Context|Why it matters)[:\s][^<]*<\/em>/gi,
    (match) => {
      const inner = match.replace(/<\/?em>/g, '');
      return `<div class="context-block">${inner}</div>`;
    }
  );

  // Add id anchors to headings so sections and sub-sections can be linked directly
  result = result.replace(/<h([23])>([^<]+)<\/h\1>/g, (_match, level, text) => {
    const id = text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
    return `<h${level} id="${id}">${text}</h${level}>`;
  });

  return result;
}
