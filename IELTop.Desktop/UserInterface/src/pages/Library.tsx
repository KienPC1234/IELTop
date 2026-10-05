import { useEffect, useState } from 'react'
import { BookOpen, PencilLine, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { usePage, readTextFile, readImageFiles, readFileBase64, isBinaryDocument } from '@/hooks'
import { Confirm, EmptyState, ErrorBar, FileImport, PageHeader } from '@/components/shared'
import { Field } from '@/components/Field'
import Editor from '@/pages/Editor'

/// The Paper Library and the Exam Editor share this screen: the editor is one
/// tab of the library rather than a sidebar entry of its own, so the sidebar
/// stays short. Every action the library had is unchanged; only the frame it
/// sits in moved.
export default function Library({ onNavigate }) {
  const page = usePage('library.snapshot')
  const [confirm, setConfirm] = useState(null)
  const [tab, setTab] = useState('library')

  useEffect(() => {
    const target = page.data?.navigateTo
    if (!target) return
    // Opening a paper for editing switches the tab; "editor" is no longer a
    // sidebar page, so it is not forwarded to the shell.
    if (target === 'editor') {
      setTab('editor')
      return
    }
    onNavigate?.(target)
  }, [page.data?.navigateTo, onNavigate])

  const d = page.data
  if (page.loading && !d) return <div className="text-muted-foreground">Loading the library...</div>
  if (page.error && !d) return <div className="text-destructive">{page.error}</div>
  if (!d) return null

  async function importFiles(files) {
    for (const file of files) {
      try {
        if (isBinaryDocument(file.name)) {
          const { name, base64 } = await readFileBase64(file)
          await page.run('library.importBinary', { fileName: name, base64 })
        } else {
          const { name, content } = await readTextFile(file)
          await page.run('library.importContent', { fileName: name, content })
        }
      } catch (e) {
        console.error(e)
      }
    }
  }

  async function readImages(files) {
    const images = await readImageFiles(files)
    if (images.length === 0) return
    await page.run('library.readImages', { images })
  }

  return (
    <div className="flex flex-col gap-5">
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />

      <PageHeader title="Library" description={d.librarySummary} />

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="library" className="gap-1.5">
            <BookOpen className="h-4 w-4" />
            Papers
          </TabsTrigger>
          <TabsTrigger value="editor" className="gap-1.5">
            <PencilLine className="h-4 w-4" />
            Editor
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {tab === 'editor' ? (
        <Editor onNavigate={onNavigate} />
      ) : (
        <>
      <Card>
        <CardContent className="flex flex-wrap items-center gap-2.5">
          <Input
            className="max-w-xs"
            placeholder="Search title or tag"
            value={d.searchText}
            onChange={(e) => page.run('library.setSearch', { value: e.target.value })}
          />
          <Select value={d.selectedCategory} onValueChange={(v) => page.run('library.setCategory', { value: v })}>
            <SelectTrigger className="w-[180px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              {d.categories.map((c) => <SelectItem key={c} value={c}>{c}</SelectItem>)}
            </SelectContent>
          </Select>
          <Select value={d.selectedSkill} onValueChange={(v) => page.run('library.setSkill', { value: v })}>
            <SelectTrigger className="w-[160px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              {d.skills.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}
            </SelectContent>
          </Select>
          <Button variant="outline" onClick={() => page.run('library.clearFilters')}>Clear filters</Button>
          <Button variant="outline" onClick={() => page.run('library.reload')}>
            <RefreshCw className="h-4 w-4" /> Reload
          </Button>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          {d.rows.length === 0 ? (
            <EmptyState title="No papers match" body="Clear the filters or import a file below." />
          ) : (
            <ul className="flex flex-col gap-2">
              {d.rows.map((row) => (
                <li key={row.title} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-card p-3.5 transition-colors hover:border-primary/30 hover:bg-muted/40">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2 text-sm font-semibold leading-relaxed">
                      <span className="min-w-0 break-words">{row.title}</span>
                      <Badge variant="secondary">{row.isUserPaper ? 'Yours' : 'Built in'}</Badge>
                    </div>
                    <p className="mt-0.5 text-sm leading-relaxed text-muted-foreground">{row.detail}</p>
                  </div>
                  <div className="flex flex-wrap justify-end gap-2">
                    <Button size="sm" onClick={() => page.run('library.startPaper', { title: row.title })}>Use</Button>
                    <Button size="sm" variant="outline" onClick={() => page.run('library.addToBasket', { title: row.title })}>Add to basket</Button>
                    <Button size="sm" variant="outline" onClick={() => page.run('library.duplicatePaper', { title: row.title })}>Duplicate</Button>
                    <Button size="sm" variant="outline" onClick={() => page.run('library.editPaper', { title: row.title })}>Edit</Button>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={!d.canUseAi || page.busy}
                      title={d.aiHint}
                      onClick={() => page.run('library.aiCheckPaper', { title: row.title })}
                    >
                      AI check
                    </Button>
                    {row.isUserPaper && (
                      <Button
                        size="sm"
                        variant="destructive"
                        onClick={() => setConfirm({
                          title: 'Delete paper',
                          body: `Delete ${row.title} from this computer?`,
                          onConfirm: async () => {
                            setConfirm(null)
                            await page.run('library.deletePaper', { title: row.title })
                          },
                        })}
                      >
                        Delete
                      </Button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-base">Custom test basket</CardTitle></CardHeader>
        <CardContent className="flex flex-col gap-3">
          <p className="text-sm leading-relaxed text-muted-foreground">{d.basketLabel}</p>
          {d.basket.length > 0 && (
            <ul className="flex flex-col gap-2">
              {d.basket.map((b) => (
                <li key={b.paperTitle} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3">
                  <div className="min-w-0 flex-1">
                    <span className="text-sm font-semibold">{b.paperTitle}</span>
                    <span className="ml-2 text-sm text-muted-foreground">{b.detail}</span>
                  </div>
                  <Button size="sm" variant="outline" onClick={() => page.run('library.removeFromBasket', { title: b.paperTitle })}>Remove</Button>
                </li>
              ))}
            </ul>
          )}
          <div className="flex flex-wrap gap-2">
            <Button disabled={!d.hasBasket} onClick={() => page.run('library.startBasket')}>Start custom test</Button>
            <Button variant="outline" disabled={!d.hasBasket} onClick={() => page.run('library.clearBasket')}>Clear basket</Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle className="text-base">Import and draft</CardTitle></CardHeader>
        <CardContent className="flex flex-col gap-3">
          <FileImport label="Import files" accept=".txt,.md,.json,.csv,.pdf,.docx" disabled={page.busy} onFiles={importFiles} />
          <FileImport label="Read pictures" accept=".png,.jpg,.jpeg" disabled={page.busy || !d.canUseVision} onFiles={readImages} />
          <p className="text-sm leading-relaxed text-muted-foreground">
            Supported: txt, md, json, csv, pdf, docx. Text files save as offline drafts you can edit. No model is needed to import.
          </p>
          <p className="text-sm leading-relaxed text-muted-foreground">{d.visionHint}</p>

          <Field label="Paste text">
            <Textarea
              rows={6}
              className="align-top"
              value={d.pasteText}
              placeholder="Paste a passage or task here, then build a draft."
              onChange={(e) => page.run('library.setPasteText', { value: e.target.value })}
            />
          </Field>

          <div className="flex flex-wrap items-end gap-2.5">
            <Field label="Draft title (optional)" className="min-w-[200px] flex-1">
              <Input
                placeholder="Draft title (optional)"
                value={d.draftTitle}
                onChange={(e) => page.run('library.setDraftTitle', { value: e.target.value })}
              />
            </Field>
            <Select value={d.draftSkill} onValueChange={(v) => page.run('library.setDraftSkill', { value: v })}>
              <SelectTrigger className="w-[150px]"><SelectValue /></SelectTrigger>
              <SelectContent>
                {d.draftSkills.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}
              </SelectContent>
            </Select>
            <Button disabled={page.busy} onClick={() => page.run('library.buildDraftFromPaste')}>Build draft</Button>
            <Button
              variant="outline"
              disabled={!d.canUseAi || page.busy}
              title={d.aiHint}
              onClick={() => page.run('library.aiFormatPaste')}
            >
              AI draft questions
            </Button>
            <Button variant="outline" onClick={() => page.run('library.newManualTemplate')}>Manual template</Button>
            {page.busy && <Button variant="destructive" size="sm" onClick={() => page.run('library.cancelAi')}>Stop</Button>}
          </div>
          <p className="text-sm leading-relaxed text-muted-foreground">{d.aiHint}</p>

          <Separator className="my-1" />

          <Field label="JSON draft">
            <Textarea
              className="font-mono align-top"
              rows={6}
              value={d.draftJson}
              placeholder="Make a template or import JSON, then save it here."
              onChange={(e) => page.run('library.setDraftJson', { value: e.target.value })}
            />
          </Field>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => page.run('library.saveDraftJson')}>Save draft</Button>
            <Button variant="outline" onClick={() => page.run('library.exportShown')}>Export shown</Button>
            <Button variant="outline" onClick={() => page.run('library.openExportFolder')}>Open export folder</Button>
          </div>
        </CardContent>
      </Card>

      {d.statusMessage && <p className="text-sm leading-relaxed text-muted-foreground">{d.statusMessage}</p>}
        </>
      )}

      <Confirm
        open={!!confirm}
        title={confirm?.title ?? ''}
        body={confirm?.body ?? ''}
        onCancel={() => setConfirm(null)}
        onConfirm={() => confirm?.onConfirm?.()}
      />
    </div>
  )
}
