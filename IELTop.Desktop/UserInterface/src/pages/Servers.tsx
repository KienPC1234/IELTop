import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Checkbox } from '@/components/ui/checkbox'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { usePage } from '@/hooks'
import { EmptyState, ErrorBar, PageHeader } from '@/components/shared'

/// Browse IELTop content servers, connect, and download papers. Servers can be
/// anonymous, need an access code, or need a login. Secrets stay on this machine.
export default function Servers({ onNavigate }) {
  const page = usePage('servers.snapshot')
  const d = page.data
  if (page.loading && !d) return <div className="text-muted-foreground">Loading servers...</div>
  if (page.error && !d) return <div className="text-destructive">{page.error}</div>
  if (!d) return null

  const run = page.run

  return (
    <div className="flex flex-col gap-5">
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />
      <PageHeader title="Servers" description="Download shared papers, then run them in Mock Test." />

      <Card>
        <CardHeader><CardTitle className="text-base">Server list</CardTitle></CardHeader>
        <CardContent className="flex flex-col gap-2.5">
          <ul className="flex flex-col gap-2">
            {d.servers.map((s) => (
              <li key={s.url} className={`flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3 transition-colors ${d.selectedServerUrl === s.url ? 'border-primary/50 bg-primary/[0.06]' : 'hover:border-primary/25 hover:bg-muted/40'}`}>
                <button type="button" className="min-w-0 flex-1 rounded text-left" onClick={() => run('servers.select', { url: s.url })}>
                  <span className="block text-sm font-semibold leading-relaxed">{s.name}</span>
                  <span className="block truncate text-sm leading-relaxed text-muted-foreground">{s.detail} | {s.url}</span>
                </button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={async () => {
                    await run('servers.select', { url: s.url })
                    await run('servers.connect')
                  }}
                >
                  Connect
                </Button>
              </li>
            ))}
          </ul>
          {d.serverInfo && <p className="text-sm leading-relaxed text-muted-foreground">{d.serverInfo}</p>}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-base">Papers from this server</CardTitle></CardHeader>
        <CardContent className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2.5">
            <Input
              className="max-w-xs"
              placeholder="Search title"
              value={d.searchText}
              onChange={(e) => run('servers.setSearch', { value: e.target.value })}
            />
            <Select value={d.selectedCategory} onValueChange={(v) => run('servers.setCategory', { value: v })}>
              <SelectTrigger className="w-[180px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                {d.categories.map((c) => <SelectItem key={c} value={c}>{c}</SelectItem>)}
              </SelectContent>
            </Select>
            <Button variant="outline" disabled={page.busy} onClick={() => run('servers.connect')}>Connect</Button>
            <Button variant="outline" disabled={!d.hasPapers || page.busy} onClick={() => run('servers.downloadAll')}>Download all</Button>
            {page.busy && <Button variant="destructive" size="sm" onClick={() => run('servers.cancel')}>Stop</Button>}
          </div>

          {d.papers.length === 0 ? (
            <EmptyState title="No papers listed" body="Pick a server and press Connect to list its papers." />
          ) : (
            <ul className="flex flex-col gap-2">
              {d.papers.map((p) => (
                <li key={p.id} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3 transition-colors hover:border-primary/25 hover:bg-muted/40">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2 text-sm font-semibold leading-relaxed">
                      <span className="min-w-0 break-words">{p.title}</span>
                      {p.isDownloaded && <Badge variant="secondary">Downloaded</Badge>}
                      {p.isUpdateAvailable && <Badge variant="outline" className="border-amber-500/50 text-amber-700 dark:text-amber-300">Update</Badge>}
                    </div>
                    <p className="mt-0.5 text-sm leading-relaxed text-muted-foreground">{p.summary}</p>
                  </div>
                  <Button size="sm" disabled={page.busy} onClick={() => run('servers.downloadPaper', { id: p.id })}>
                    {p.isDownloaded ? 'Re-download' : 'Download'}
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-base">Add a server</CardTitle></CardHeader>
        <CardContent className="flex flex-col gap-3">
          <div className="grid grid-cols-1 gap-3.5 md:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="srv-name">Name</Label>
              <Input id="srv-name" placeholder="My school server" value={d.newName} onChange={(e) => run('servers.setNewName', { value: e.target.value })} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="srv-url">Address</Label>
              <Input id="srv-url" placeholder="https://example.com" value={d.newUrl} onChange={(e) => run('servers.setNewUrl', { value: e.target.value })} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label>Auth mode</Label>
              <Select value={d.authMode} onValueChange={(v) => run('servers.setAuthMode', { value: v })}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {d.authModes.map((m) => <SelectItem key={m} value={m}>{m}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="srv-user">Username (login mode)</Label>
              <Input id="srv-user" placeholder="Student name" value={d.username} onChange={(e) => run('servers.setUsername', { value: e.target.value })} />
            </div>
            <div className="flex flex-col gap-1.5 md:col-span-2">
              <Label htmlFor="srv-secret">Access code or password</Label>
              <Input
                id="srv-secret"
                type="password"
                value={d.secret}
                placeholder={d.hasSecret ? 'Saved' : 'Enter the code from your teacher'}
                onChange={(e) => run('servers.setSecret', { value: e.target.value })}
              />
            </div>
          </div>
          <label className="flex cursor-pointer items-center gap-2 text-sm leading-relaxed">
            <Checkbox
              checked={d.allowInsecure}
              onCheckedChange={(v) => run('servers.setAllowInsecure', { value: v === true })}
            />
            Allow plain http on a remote host
          </label>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => run('servers.add')}>Save server</Button>
            <Button variant="outline" disabled={!d.hasSelection} onClick={() => run('servers.remove')}>Remove saved server</Button>
          </div>
        </CardContent>
      </Card>

      {d.statusMessage && <p className="text-sm leading-relaxed text-muted-foreground">{d.statusMessage}</p>}
    </div>
  )
}
