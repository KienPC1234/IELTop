import { useMemo } from 'react'
import { marked } from 'marked'
import katex from 'katex'

marked.setOptions({
  gfm: true,
  breaks: true,
})

interface MarkdownRendererProps {
  content: string
  className?: string
}

function renderMathAndMarkdown(rawText: string): string {
  if (!rawText) return ''

  // 1. Extract display math blocks: $$ ... $$ or \[ ... \]
  const mathPlaceholders: { token: string; html: string }[] = []
  let counter = 0

  let text = rawText.replace(/\$\$([\s\S]*?)\$\$|\\\[([\s\S]*?)\\\]/g, (_, p1, p2) => {
    const math = p1 || p2
    const token = `%%KATEX_BLOCK_${counter++}%%`
    try {
      const html = `<div class="katex-display my-2 overflow-x-auto">${katex.renderToString(math.trim(), { displayMode: true, throwOnError: false })}</div>`
      mathPlaceholders.push({ token, html })
    } catch {
      mathPlaceholders.push({ token, html: `<div class="text-destructive font-mono text-xs">$$${math}$$</div>` })
    }
    return token
  })

  // 2. Extract inline math: $...$ or \(...\)
  text = text.replace(/(?<![\\\w])\$([^\$\n\s][^\$\n]*?[^\$\n\s]|\S)\$|\\\(([\s\S]*?)\\\)/g, (match, p1, p2) => {
    const math = (p1 || p2 || '').trim()
    if (!math) return match

    // Currency safeguard: e.g. $10, $5.99, $100k, $2 million
    if (/^\d+(\.\d+)?(\s*(?:k|m|b|million|billion|thousand|hundred|dollars?|usd|aud|gbp|eur|vnd))?$/i.test(math)) {
      return match
    }

    // Two prices safeguard: if it contains normal English words and no backslash/operators, it's prose between two prices
    if (!math.includes('\\') && /\b(?:and|or|for|the|in|with|to|from|is|are|was|were|per|each)\b/i.test(math)) {
      return match
    }

    const token = `%%KATEX_INLINE_${counter++}%%`
    try {
      const html = `<span class="katex-inline">${katex.renderToString(math, { displayMode: false, throwOnError: false })}</span>`
      mathPlaceholders.push({ token, html })
      return token
    } catch {
      return match
    }
  })

  // 3. Parse Markdown
  let html = marked.parse(text) as string

  // 4. Restore KaTeX replacements
  for (const { token, html: mathHtml } of mathPlaceholders) {
    html = html.replace(token, mathHtml)
  }

  return html
}

export function MarkdownRenderer({ content, className = '' }: MarkdownRendererProps) {
  const renderedHtml = useMemo(() => renderMathAndMarkdown(content), [content])

  return (
    <div
      className={`markdown-body text-xs leading-relaxed [&>*:first-child]:mt-0 [&>*:last-child]:mb-0 [&_p]:my-1.5 [&_h1]:text-base [&_h1]:font-bold [&_h1]:mt-3 [&_h1]:mb-1.5 [&_h2]:text-sm [&_h2]:font-bold [&_h2]:mt-2.5 [&_h2]:mb-1 [&_h3]:text-xs [&_h3]:font-bold [&_h3]:mt-2 [&_h3]:mb-1 [&_ul]:list-disc [&_ul]:pl-5 [&_ul]:my-1.5 [&_ul]:space-y-0.5 [&_ol]:list-decimal [&_ol]:pl-5 [&_ol]:my-1.5 [&_ol]:space-y-0.5 [&_li]:my-0.5 [&_strong]:font-semibold [&_strong]:text-foreground [&_code]:font-mono [&_code]:bg-muted/80 [&_code]:px-1.5 [&_code]:py-0.5 [&_code]:rounded [&_code]:text-[11px] [&_pre]:bg-muted/70 [&_pre]:p-2.5 [&_pre]:rounded-md [&_pre]:border [&_pre]:overflow-x-auto [&_pre]:my-2 [&_pre_code]:bg-transparent [&_pre_code]:p-0 [&_blockquote]:border-l-2 [&_blockquote]:border-primary/50 [&_blockquote]:pl-3 [&_blockquote]:italic [&_blockquote]:my-1.5 [&_blockquote]:text-muted-foreground [&_table]:w-full [&_table]:border-collapse [&_table]:border [&_table]:border-border [&_table]:my-2 [&_th]:border [&_th]:border-border [&_th]:p-1.5 [&_th]:bg-muted/50 [&_th]:font-semibold [&_td]:border [&_td]:border-border [&_td]:p-1.5 [&_hr]:my-3 [&_hr]:border-border ${className}`}
      dangerouslySetInnerHTML={{ __html: renderedHtml }}
    />
  )
}
