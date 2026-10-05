import { useEffect, useState } from 'react'
import {
  Activity,
  CheckCircle2,
  AlertCircle,
  RefreshCw,
  FolderOpen,
  Save,
  Copy,
  Stethoscope,
  Tag,
} from 'lucide-react'
import { call } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Field } from '@/components/Field'
import { EmptyState } from '@/components/shared'
import { pageErrorCount, logNote } from '@/diagnostics'

/// The Diagnostics tab. It exists so a session can be checked in one look
/// instead of opening windows and guessing: the health counters, the slowest
/// bridge calls, the database check, and the log tail, all on one screen.
export default function DiagnosticsPanel({ onError }) {
  const [data, setData] = useState<any>(null)
  const [busy, setBusy] = useState(false)
  const [tailLines, setTailLines] = useState('200')
  const [mark, setMark] = useState('')

  async function run(method: string, args: any = {}) {
    setBusy(true)
    try {
      const next = await call(method, args)
      if (next) setData(next)
      return next
    } catch (e: any) {
      onError?.(e.message)
      return null
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    run('diagnostics.snapshot', { tailLines: 200 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const healthy = data?.isHealthy
  const pageErrors = pageErrorCount()

  return (
    <div className="flex flex-col gap-5">
      {/* Health at a glance */}
      <Card>
        <CardHeader className="flex-row items-center justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2 text-base font-semibold">
              <Activity className="h-4 w-4" />
              Session health
            </CardTitle>
            <CardDescription>
              {data ? `Session ${data.sessionId}, up ${data.uptimeLabel}` : 'Reading the session...'}
            </CardDescription>
          </div>
          {data && (
            <Badge variant={healthy ? 'secondary' : 'destructive'} className="gap-1 text-xs">
              {healthy ? <CheckCircle2 className="h-3.5 w-3.5" /> : <AlertCircle className="h-3.5 w-3.5" />}
              {healthy ? 'Healthy' : 'Needs a look'}
            </Badge>
          )}
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Stat label="Bridge calls" value={data?.bridgeCalls ?? 0} />
          <Stat label="Bridge failures" value={data?.bridgeFailures ?? 0} tone={data?.bridgeFailures ? 'bad' : 'good'} />
          <Stat label="Unhandled errors" value={data?.unhandledErrors ?? 0} tone={data?.unhandledErrors ? 'bad' : 'good'} />
          <Stat label="Page errors" value={pageErrors} tone={pageErrors ? 'bad' : 'good'} />
          <Stat label="Log lines" value={data?.loggedLines ?? 0} />
          <Stat label="Working set" value={data ? `${data.workingSetMb} MB` : '-'} />
          <Stat label="Database" value={data?.databaseSizeLabel ?? '-'} />
          <Stat label="Methods used" value={data?.bridgeMethods ?? 0} />
        </CardContent>
      </Card>

      {/* Controls */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base font-semibold">Controls</CardTitle>
          <CardDescription>
            Detail level applies from now on. Export copies the newest log for a bug report.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <Field label="Log detail" className="w-[150px]">
              <Select value={data?.level ?? 'Info'} onValueChange={(v) => run('diagnostics.setLevel', { level: v })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(data?.levels ?? []).map((l: string) => (
                    <SelectItem key={l} value={l}>{l}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Tail lines" className="w-[120px]">
              <Select
                value={tailLines}
                onValueChange={(v) => {
                  setTailLines(v)
                  run('diagnostics.snapshot', { tailLines: Number(v) })
                }}
              >
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {['100', '200', '500', '1000'].map((n) => (
                    <SelectItem key={n} value={n}>{n}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Button variant="outline" className="gap-1.5" disabled={busy} onClick={() => run('diagnostics.snapshot', { tailLines: Number(tailLines) })}>
              <RefreshCw className="h-4 w-4" />
              Refresh
            </Button>
            <Button variant="outline" className="gap-1.5" disabled={busy} onClick={() => run('diagnostics.checkDatabase')}>
              <Stethoscope className="h-4 w-4" />
              Check database
            </Button>
          </div>

          <div className="flex flex-wrap items-end gap-3">
            <Field label="Add a marker to the log" className="min-w-[240px] flex-1">
              <input
                className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm"
                placeholder="What you just did, for example: started a Reading test"
                value={mark}
                onChange={(e) => setMark(e.target.value)}
              />
            </Field>
            <Button
              variant="outline"
              className="gap-1.5"
              disabled={busy}
              onClick={() => {
                logNote(mark)
                run('diagnostics.mark', { note: mark })
                setMark('')
              }}
            >
              <Tag className="h-4 w-4" />
              Mark
            </Button>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Button variant="outline" className="gap-1.5" disabled={busy} onClick={() => run('diagnostics.export', { path: '' })}>
              <Save className="h-4 w-4" />
              Export log
            </Button>
            <Button variant="outline" className="gap-1.5" disabled={busy} onClick={() => run('diagnostics.openFolder')}>
              <FolderOpen className="h-4 w-4" />
              Open log folder
            </Button>
            <span className="min-w-0 flex-1 truncate font-mono text-xs text-muted-foreground" title={data?.logDirectory}>
              {data?.logDirectory ?? ''}
            </span>
          </div>

          {data?.databaseIntegrity && data.databaseIntegrity !== 'not checked' && (
            <p className="text-sm text-muted-foreground">Database: {data.databaseIntegrity}</p>
          )}
          {data?.statusMessage && <p className="text-sm text-muted-foreground">{data.statusMessage}</p>}
        </CardContent>
      </Card>

      {/* Slowest / failing bridge calls */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base font-semibold">Call timings</CardTitle>
          <CardDescription>Slowest first. A rising max time points at the step that needs work.</CardDescription>
        </CardHeader>
        <CardContent>
          {(data?.slowCalls ?? []).length === 0 ? (
            <EmptyState title="No calls yet" body="Use the app, then refresh to see timings." />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-xs uppercase text-muted-foreground">
                    <th className="py-1.5 pr-3">Method</th>
                    <th className="py-1.5 pr-3 text-right">Calls</th>
                    <th className="py-1.5 pr-3 text-right">Failed</th>
                    <th className="py-1.5 pr-3 text-right">Avg ms</th>
                    <th className="py-1.5 pr-3 text-right">Max ms</th>
                    <th className="py-1.5">Last error</th>
                  </tr>
                </thead>
                <tbody>
                  {(data?.slowCalls ?? []).map((row: any) => (
                    <tr key={row.method} className="border-b border-border/50 last:border-0">
                      <td className="py-1.5 pr-3 font-mono text-xs">{row.method}</td>
                      <td className="py-1.5 pr-3 text-right tabular-nums">{row.count}</td>
                      <td className={`py-1.5 pr-3 text-right tabular-nums ${row.failed ? 'text-destructive' : ''}`}>{row.failed}</td>
                      <td className="py-1.5 pr-3 text-right tabular-nums">{row.avgMs}</td>
                      <td className="py-1.5 pr-3 text-right tabular-nums">{row.maxMs}</td>
                      <td className="max-w-[280px] truncate py-1.5 text-xs text-muted-foreground" title={row.lastError}>{row.lastError}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Log tail */}
      <Card>
        <CardHeader className="flex-row items-center justify-between gap-3">
          <div>
            <CardTitle className="text-base font-semibold">Log tail</CardTitle>
            <CardDescription>{data?.latestFile ?? ''}</CardDescription>
          </div>
          <Button variant="outline" className="gap-1.5" onClick={() => copyTail(data?.logTail ?? '')}>
            <Copy className="h-4 w-4" />
            Copy
          </Button>
        </CardHeader>
        <CardContent>
          <pre className="max-h-[420px] overflow-auto whitespace-pre-wrap rounded-md border border-border bg-muted/40 p-3 font-mono text-xs leading-relaxed">
            {data?.logTail || 'No log lines yet.'}
          </pre>
        </CardContent>
      </Card>
    </div>
  )
}

/// Copies the log tail to the clipboard, with a plain fallback for the WebView.
function copyTail(text: string) {
  if (!text) return
  try {
    if (navigator.clipboard?.writeText) {
      void navigator.clipboard.writeText(text)
      return
    }
    const area = document.createElement('textarea')
    area.value = text
    document.body.appendChild(area)
    area.select()
    document.execCommand('copy')
    document.body.removeChild(area)
  } catch {
    /* copying is optional */
  }
}

function Stat({ label, value, tone }: { label: string; value: React.ReactNode; tone?: 'good' | 'bad' }) {
  return (
    <div className="rounded-lg border border-border bg-card px-3 py-2">
      <div className="text-xs font-medium text-muted-foreground">{label}</div>
      <div className={`mt-0.5 text-lg font-bold tabular-nums ${tone === 'bad' ? 'text-destructive' : tone === 'good' ? 'text-success' : 'text-foreground'}`}>
        {value}
      </div>
    </div>
  )
}
