import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { usePage } from '@/hooks'
import { Confirm, EmptyState, ErrorBar, PageHeader } from '@/components/shared'

/// Past mock test results with band ranges and the official criteria tables.
/// Bands are practice estimates, never official scores.
export default function Results() {
  const page = usePage('results.snapshot')
  const [confirm, setConfirm] = useState(false)
  const [open, setOpen] = useState(null)

  const d = page.data
  if (page.loading && !d) return <div className="text-muted-foreground">Loading results...</div>
  if (page.error && !d) return <div className="text-destructive">{page.error}</div>
  if (!d) return null

  const detail = open != null ? d.filtered.find((x) => x.id === open) : null

  return (
    <div className="flex flex-col gap-5">
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />
      <PageHeader title="Results" description={d.summaryLabel} />

      <Card>
        <CardContent className="flex flex-wrap items-center gap-2.5">
          <Select value={d.scopeFilter} onValueChange={(v) => page.run('results.setScope', { value: v })}>
            <SelectTrigger className="w-[180px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              {d.scopeFilters.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}
            </SelectContent>
          </Select>
          <Input
            className="max-w-xs"
            placeholder="Search paper title"
            value={d.searchText}
            onChange={(e) => page.run('results.setSearch', { value: e.target.value })}
          />
          <Button variant="outline" onClick={() => page.run('results.snapshot')}>Reload</Button>
          <Button variant="outline" disabled={!d.hasAttempts} onClick={() => setConfirm(true)}>Clear all</Button>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          {d.filtered.length === 0 ? (
            <EmptyState title="No results yet" body={d.statusMessage} />
          ) : (
            <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Date</TableHead>
                  <TableHead>Paper</TableHead>
                  <TableHead>Scope</TableHead>
                  <TableHead>Band</TableHead>
                  <TableHead>Score</TableHead>
                  <TableHead>Marking</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {d.filtered.map((a) => (
                  <TableRow key={a.id}>
                    <TableCell>{a.createdAt}</TableCell>
                    <TableCell>{a.paperTitle}</TableCell>
                    <TableCell>{a.scope}</TableCell>
                    <TableCell className="font-bold">{a.bandLabel}</TableCell>
                    <TableCell>
                      <span className="inline-flex items-center gap-2">
                        {a.correct}/{a.total}
                        {a.violations > 0 && <Badge variant="outline" className="border-warning/50 text-warning">left test {a.violations}x</Badge>}
                      </span>
                    </TableCell>
                    <TableCell>
                      <span className="inline-flex items-center gap-2">
                        {a.strictness}
                        {a.writingBand && <Badge variant="secondary">Writing {a.writingBand}</Badge>}
                        {a.speakingBand && <Badge variant="secondary">Speaking {a.speakingBand}</Badge>}
                      </span>
                    </TableCell>
                    <TableCell>
                      <Button size="sm" variant="outline" onClick={() => setOpen(open === a.id ? null : a.id)}>
                        {open === a.id ? 'Hide' : 'Detail'}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            </div>
          )}

          {detail && (
            <div className="mt-4 border-t border-border pt-4">
              <h3 className="mb-1 text-sm font-semibold">Summary</h3>
              <p className="text-sm leading-relaxed text-muted-foreground">{detail.summary}</p>
              {detail.aiFeedback && (
                <>
                  <h3 className="mt-3 mb-1 text-sm font-semibold">AI feedback</h3>
                  <pre className="whitespace-pre-wrap font-sans text-sm leading-relaxed">{detail.aiFeedback}</pre>
                </>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-base">Marking criteria</CardTitle></CardHeader>
        <CardContent className="flex flex-col gap-4">
          <p className="text-sm leading-relaxed text-muted-foreground">{d.criteriaHint}</p>
          <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
            <div>
              <h3 className="mb-2 text-sm font-semibold">Writing</h3>
              <ul className="list-disc space-y-1 pl-5 text-sm leading-relaxed text-muted-foreground">
                {d.writingTable.map((line, i) => <li key={i}>{line}</li>)}
              </ul>
            </div>
            <div>
              <h3 className="mb-2 text-sm font-semibold">Speaking</h3>
              <ul className="list-disc space-y-1 pl-5 text-sm leading-relaxed text-muted-foreground">
                {d.speakingTable.map((line, i) => <li key={i}>{line}</li>)}
              </ul>
            </div>
          </div>
        </CardContent>
      </Card>

      <Confirm
        open={confirm}
        title="Clear results"
        body="Delete all finished test results from this computer?"
        confirmLabel="Clear all"
        onCancel={() => setConfirm(false)}
        onConfirm={async () => {
          setConfirm(false)
          await page.run('results.clearAll')
        }}
      />
    </div>
  )
}
