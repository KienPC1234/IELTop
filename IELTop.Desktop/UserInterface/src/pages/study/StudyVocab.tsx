import { useEffect, useState } from 'react'
import { Search } from 'lucide-react'
import { call } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { EmptyState } from '@/components/shared'

/// The course vocabulary, taken from the lesson sheets (word, form, meaning,
/// example, IPA, derivatives). Browsing and search are offline and need no
/// model, so this tab always works.
export default function StudyVocab({ units, onError }) {
  const [snapshot, setSnapshot] = useState<any>(null)
  const [query, setQuery] = useState('')
  const [unit, setUnit] = useState('')
  const [busy, setBusy] = useState(false)

  async function run(args: any = {}) {
    setBusy(true)
    try {
      const next = await call('study.vocab.snapshot', args)
      if (next) setSnapshot(next)
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    run({ query: '', unit: '' })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const words = snapshot?.words ?? []

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardContent className="flex flex-wrap items-center gap-2.5">
          <Input
            className="max-w-xs"
            placeholder="Search word or meaning"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') run({ query, unit })
            }}
          />
          <Button variant="outline" className="gap-1.5" disabled={busy} onClick={() => run({ query, unit })}>
            <Search className="h-4 w-4" />
            Search
          </Button>
          <Select
            value={unit || 'all'}
            onValueChange={(v) => {
              const next = v === 'all' ? '' : v
              setUnit(next)
              run({ query, unit: next })
            }}
          >
            <SelectTrigger className="w-[200px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All lessons</SelectItem>
              {(units ?? []).map((u: any) => (
                <SelectItem key={u.slug} value={u.slug}>{u.title}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Badge variant="secondary" className="text-xs">{snapshot?.total ?? 0} words</Badge>
        </CardContent>
      </Card>

      {words.length === 0 ? (
        <EmptyState
          title="No words match"
          body="Try another spelling, or clear the search to see the whole course list."
        />
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {words.map((w: any, i: number) => (
            <Card key={`${w.word}-${i}`}>
              <CardContent className="flex flex-col gap-1.5">
                <div className="flex flex-wrap items-baseline gap-2">
                  <span className="text-base font-bold">{w.word}</span>
                  {w.form && <Badge variant="outline" className="text-xs">{w.form}</Badge>}
                  {w.ipa && <span className="font-mono text-xs text-muted-foreground">{w.ipa}</span>}
                </div>
                {w.meaning && <p className="text-sm leading-relaxed text-foreground">{w.meaning}</p>}
                {w.example && (
                  <p className="text-sm leading-relaxed text-muted-foreground">
                    <span className="font-medium">Text:</span> {w.example}
                  </p>
                )}
                {w.extraExample && (
                  <p className="text-sm leading-relaxed text-muted-foreground">
                    <span className="font-medium">Also:</span> {w.extraExample}
                  </p>
                )}
                {w.derivatives && (
                  <p className="text-xs text-muted-foreground">
                    <span className="font-medium">Family:</span> {w.derivatives}
                  </p>
                )}
                <span className="mt-0.5 text-xs text-muted-foreground/70">{w.unit}</span>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
