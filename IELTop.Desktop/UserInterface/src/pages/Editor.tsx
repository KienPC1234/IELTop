import { useEffect, useState } from 'react'
import {
  FileText,
  Layers,
  ListChecks,
  Save,
  Play,
  CheckCircle2,
  Copy,
  Plus,
  Trash2,
  AlertTriangle,
  Sparkles,
  Upload,
  Image,
  Clock,
  HelpCircle,
  Headphones,
  BookOpen,
  PenTool,
  Mic,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Badge } from '@/components/ui/badge'
import { Separator } from '@/components/ui/separator'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { usePage, readTextFile, readImageFiles, readFileBase64, isBinaryDocument } from '@/hooks'
import { Confirm, ErrorBar, FileImport, PageHeader } from '@/components/shared'
import { Field } from '@/components/Field'

export default function Editor({ onNavigate }) {
  const page = usePage('editor.snapshot')
  const [confirm, setConfirm] = useState(null)
  const [activeTab, setActiveTab] = useState('parts')

  const navigate = page.data?.navigateTo
  useEffect(() => {
    if (navigate) onNavigate?.(navigate)
  }, [navigate, onNavigate])

  const d = page.data

  if (page.loading && !d) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted-foreground">
        Loading test paper editor...
      </div>
    )
  }

  if (page.error && !d) {
    return <div className="p-4 text-sm text-destructive">{page.error}</div>
  }

  if (!d) return null

  const setPaper = (field, value) => page.run('editor.setPaperField', { field, value })
  const part = d.selectedPartIndex >= 0 ? d.parts[d.selectedPartIndex] : null
  const question = part && d.selectedQuestionIndex >= 0 ? part.questions[d.selectedQuestionIndex] : null

  const setPart = (field, value) =>
    page.run('editor.setPartField', { index: d.selectedPartIndex, field, value })

  const setQuestion = (field, value) =>
    page.run('editor.setQuestionField', {
      partIndex: d.selectedPartIndex,
      questionIndex: d.selectedQuestionIndex,
      field,
      value,
    })

  async function importIntoPart(files) {
    if (!files.length) return
    const file = files[0]
    try {
      if (isBinaryDocument(file.name)) {
        const { name, base64 } = await readFileBase64(file)
        await page.run('editor.importBinary', { fileName: name, base64 })
      } else {
        const { name, content } = await readTextFile(file)
        await page.run('editor.importContent', { fileName: name, content })
      }
    } catch (e) {
      console.error(e)
    }
  }

  async function readIntoPart(files) {
    const images = await readImageFiles(files)
    if (images.length === 0) return
    await page.run('editor.readImages', { images })
  }

  // Calculate paper statistics
  const totalQuestions = (d.parts ?? []).reduce(
    (acc, p) => acc + (p.questions?.length || 0),
    0
  )
  const totalMinutes = (d.parts ?? []).reduce(
    (acc, p) => acc + (Number(p.minutes) || 0),
    0
  )

  const skillIcon = (skill) => {
    switch (skill?.toLowerCase()) {
      case 'listening':
        return <Headphones className="h-3.5 w-3.5 text-blue-500" />
      case 'reading':
        return <BookOpen className="h-3.5 w-3.5 text-emerald-500" />
      case 'writing':
        return <PenTool className="h-3.5 w-3.5 text-amber-500" />
      case 'speaking':
        return <Mic className="h-3.5 w-3.5 text-purple-500" />
      default:
        return <Layers className="h-3.5 w-3.5 text-muted-foreground" />
    }
  }

  return (
    <div className="flex flex-col gap-5 pb-16">
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />

      {/* Top Action Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b border-border pb-4">
        <div className="min-w-0">
          <div className="flex items-center gap-2.5">
            <h1 className="truncate text-xl font-bold tracking-tight text-foreground">
              {d.paperTitle || 'Untitled Paper'}
            </h1>
            <Badge variant="outline" className="text-xs">
              {d.category || 'General'}
            </Badge>
            {d.validationIssues.length > 0 ? (
              <Badge variant="outline" className="border-amber-500/50 text-amber-600 dark:text-amber-400 gap-1 text-xs">
                <AlertTriangle className="h-3 w-3" /> {d.validationIssues.length} issues
              </Badge>
            ) : (
              <Badge variant="outline" className="border-emerald-500/50 text-emerald-600 dark:text-emerald-400 gap-1 text-xs">
                <CheckCircle2 className="h-3 w-3" /> Valid
              </Badge>
            )}
          </div>
          <p className="mt-0.5 text-xs text-muted-foreground">
            {d.parts?.length || 0} parts · {totalQuestions} questions · {totalMinutes} min estimated
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => page.run('editor.save')} className="gap-1.5 shadow-sm">
            <Save className="h-4 w-4" /> Save
          </Button>
          <Button variant="outline" onClick={() => page.run('editor.testPaper')} className="gap-1.5">
            <Play className="h-4 w-4" /> Save and test
          </Button>
          <Button variant="outline" onClick={() => page.run('editor.validate')} className="gap-1.5">
            <CheckCircle2 className="h-4 w-4" /> Validate
          </Button>
          <Button variant="outline" onClick={() => page.run('editor.duplicate')} className="gap-1.5">
            <Copy className="h-4 w-4" /> Duplicate
          </Button>
          <Button variant="outline" onClick={() => page.run('editor.new')} className="gap-1.5">
            <Plus className="h-4 w-4" /> New paper
          </Button>
          <Button
            variant="ghost"
            className="text-muted-foreground hover:text-destructive"
            onClick={() =>
              setConfirm({
                title: 'Delete paper',
                body: `Permanently delete "${d.paperTitle}" from your computer?`,
                onConfirm: async () => {
                  setConfirm(null)
                  await page.run('editor.delete')
                },
              })
            }
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Main Tabs Navigation */}
      <Tabs value={activeTab} onValueChange={setActiveTab} className="w-full">
        <TabsList className="grid h-10 w-full grid-cols-3 p-1 bg-muted/70">
          <TabsTrigger value="overview" className="flex items-center gap-2 text-xs sm:text-sm">
            <FileText className="h-4 w-4" />
            <span>Overview & Info</span>
          </TabsTrigger>
          <TabsTrigger value="parts" className="flex items-center gap-2 text-xs sm:text-sm">
            <Layers className="h-4 w-4" />
            <span>Parts & Materials ({d.parts?.length || 0})</span>
          </TabsTrigger>
          <TabsTrigger value="questions" className="flex items-center gap-2 text-xs sm:text-sm">
            <ListChecks className="h-4 w-4" />
            <span>Question Builder ({totalQuestions})</span>
          </TabsTrigger>
        </TabsList>

        {/* Tab 1: Overview & Metadata */}
        <TabsContent value="overview" className="mt-4 flex flex-col gap-5">
          <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
            <Card className="md:col-span-2">
              <CardHeader>
                <CardTitle className="text-base font-semibold">Paper Metadata</CardTitle>
                <CardDescription>
                  General details identifying this examination paper in the library.
                </CardDescription>
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <Field label="Paper title" className="sm:col-span-2">
                    <Input
                      value={d.paperTitle}
                      placeholder="e.g. Cambridge IELTS 18 Academic Test 1"
                      onChange={(e) => setPaper('title', e.target.value)}
                    />
                  </Field>
                  <Field label="Category">
                    <Input
                      value={d.category}
                      placeholder="e.g. Academic, General Training"
                      onChange={(e) => setPaper('category', e.target.value)}
                    />
                  </Field>
                  <Field label="Level / Target band">
                    <Input
                      value={d.level}
                      placeholder="e.g. Band 6.5 - 7.5"
                      onChange={(e) => setPaper('level', e.target.value)}
                    />
                  </Field>
                  <Field label="Tags (comma separated)">
                    <Input
                      value={d.tagsText}
                      placeholder="e.g. reading, academic, cambridge"
                      onChange={(e) => setPaper('tags', e.target.value)}
                    />
                  </Field>
                  <Field label="Source attribution">
                    <Input
                      value={d.source}
                      placeholder="e.g. Cambridge University Press / Open licensed"
                      onChange={(e) => setPaper('source', e.target.value)}
                    />
                  </Field>
                </div>
              </CardContent>
            </Card>

            <div className="flex flex-col gap-5">
              <Card>
                <CardHeader>
                  <CardTitle className="text-sm font-semibold">Test Paper Breakdown</CardTitle>
                </CardHeader>
                <CardContent className="space-y-3 text-xs">
                  <div className="flex justify-between border-b pb-2">
                    <span className="text-muted-foreground">Total Parts:</span>
                    <span className="font-semibold text-foreground">{d.parts?.length || 0}</span>
                  </div>
                  <div className="flex justify-between border-b pb-2">
                    <span className="text-muted-foreground">Total Questions:</span>
                    <span className="font-semibold text-foreground">{totalQuestions}</span>
                  </div>
                  <div className="flex justify-between border-b pb-2">
                    <span className="text-muted-foreground">Exam Duration:</span>
                    <span className="font-semibold text-foreground">{totalMinutes} minutes</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Included Skills:</span>
                    <div className="flex flex-wrap gap-1 justify-end">
                      {Array.from(new Set<string>((d.parts ?? []).map((p: any) => p.skill))).map((s) => (
                        <Badge key={s} variant="secondary" className="text-[10px]">
                          {s}
                        </Badge>
                      ))}
                    </div>
                  </div>
                </CardContent>
              </Card>

              {d.validationIssues.length > 0 ? (
                <Card className="border-amber-500/30 bg-amber-500/5">
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-semibold text-amber-700 dark:text-amber-300 flex items-center gap-1.5">
                      <AlertTriangle className="h-4 w-4" /> Validation Notices
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    <ul className="list-disc pl-4 space-y-1 text-xs text-amber-700 dark:text-amber-300">
                      {d.validationIssues.map((issue, idx) => (
                        <li key={idx}>{issue}</li>
                      ))}
                    </ul>
                  </CardContent>
                </Card>
              ) : (
                <Card className="border-emerald-500/30 bg-emerald-500/5">
                  <CardContent className="py-4 flex items-center gap-2 text-xs text-emerald-700 dark:text-emerald-300 font-medium">
                    <CheckCircle2 className="h-4 w-4" /> All parts and question numbering pass validation.
                  </CardContent>
                </Card>
              )}
            </div>
          </div>
        </TabsContent>

        {/* Tab 2: Parts & Materials */}
        <TabsContent value="parts" className="mt-4 flex flex-col gap-5">
          <div className="grid grid-cols-1 gap-5 lg:grid-cols-12 items-start">
            {/* Left Column: Parts Master List */}
            <div className="lg:col-span-4 flex flex-col gap-3">
              <Card>
                <CardHeader className="pb-3">
                  <div className="flex items-center justify-between">
                    <CardTitle className="text-sm font-semibold">Parts in Paper</CardTitle>
                    <Badge variant="secondary" className="text-xs">{d.parts?.length || 0}</Badge>
                  </div>
                  <div className="flex flex-wrap gap-1.5 pt-2">
                    {d.skills.map((s) => (
                      <Button
                        key={s}
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs px-2 gap-1"
                        onClick={() => page.run('editor.addPart', { skill: s })}
                      >
                        <Plus className="h-3 w-3" /> {s}
                      </Button>
                    ))}
                  </div>
                </CardHeader>
                <CardContent className="p-2 pt-0">
                  <div className="flex flex-col gap-1.5 max-h-[580px] overflow-y-auto pr-1">
                    {(d.parts ?? []).length === 0 ? (
                      <div className="py-8 text-center text-xs text-muted-foreground">
                        No parts yet. Click a skill button above to add a part.
                      </div>
                    ) : (
                      d.parts.map((p, i) => {
                        const isSelected = i === d.selectedPartIndex
                        return (
                          <div
                            key={i}
                            onClick={() => page.run('editor.selectPart', { index: i })}
                            className={`flex cursor-pointer items-start justify-between gap-2 rounded-lg border p-3 transition-colors ${
                              isSelected
                                ? 'border-primary bg-primary/[0.08] shadow-xs'
                                : 'hover:border-primary/30 hover:bg-muted/40'
                            }`}
                          >
                            <div className="min-w-0 flex-1 space-y-1">
                              <div className="flex items-center gap-1.5">
                                {skillIcon(p.skill)}
                                <span className="font-semibold text-xs tracking-tight text-foreground truncate">
                                  {p.id || `Part ${i + 1}`} · {p.title || 'Untitled'}
                                </span>
                              </div>
                              <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
                                <span className="flex items-center gap-0.5">
                                  <Clock className="h-3 w-3" /> {p.minutes}m
                                </span>
                                <span>·</span>
                                <span className="flex items-center gap-0.5">
                                  <HelpCircle className="h-3 w-3" /> {p.questions?.length || 0} questions
                                </span>
                              </div>
                            </div>
                            <Button
                              size="icon"
                              variant="ghost"
                              className="h-6 w-6 text-muted-foreground hover:text-destructive shrink-0"
                              onClick={(e) => {
                                e.stopPropagation()
                                page.run('editor.removePart', { index: i })
                              }}
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        )
                      })
                    )}
                  </div>
                </CardContent>
              </Card>
            </div>

            {/* Right Column: Selected Part Editor */}
            <div className="lg:col-span-8 flex flex-col gap-4">
              {!part ? (
                <Card className="flex h-72 items-center justify-center text-center p-6 text-muted-foreground">
                  <div>
                    <Layers className="h-8 w-8 mx-auto mb-2 opacity-50" />
                    <p className="text-sm font-medium">Select a part on the left to edit its materials and parameters.</p>
                  </div>
                </Card>
              ) : (
                <>
                  <Card>
                    <CardHeader className="pb-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          {skillIcon(part.skill)}
                          <CardTitle className="text-base font-semibold">
                            Part Details: {part.id} - {part.title || 'Untitled'}
                          </CardTitle>
                        </div>
                        <Badge variant="secondary">{part.skill}</Badge>
                      </div>
                    </CardHeader>
                    <CardContent className="flex flex-col gap-4">
                      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                        <Field label="Part ID">
                          <Input
                            value={part.id}
                            placeholder="e.g. R1, L2, W1"
                            onChange={(e) => setPart('id', e.target.value)}
                          />
                        </Field>
                        <Field label="Skill">
                          <Select value={part.skill} onValueChange={(v) => setPart('skill', v)}>
                            <SelectTrigger><SelectValue /></SelectTrigger>
                            <SelectContent>
                              {d.skills.map((s) => (
                                <SelectItem key={s} value={s}>{s}</SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </Field>
                        <Field label="Task type">
                          <Input
                            value={part.taskType}
                            placeholder="e.g. Matching Headings"
                            onChange={(e) => setPart('taskType', e.target.value)}
                          />
                        </Field>
                        <Field label="Time (minutes)">
                          <Input
                            type="number"
                            min="1"
                            value={part.minutes}
                            onChange={(e) => setPart('minutes', e.target.value)}
                          />
                        </Field>
                      </div>

                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        <Field label="Part title">
                          <Input
                            value={part.title}
                            placeholder="e.g. The Roman Shipbuilding Tradition"
                            onChange={(e) => setPart('title', e.target.value)}
                          />
                        </Field>
                        <Field label="Audio file name (Listening)">
                          <Input
                            value={part.audioFile}
                            placeholder="e.g. track_part1.mp3"
                            onChange={(e) => setPart('audioFile', e.target.value)}
                          />
                        </Field>
                      </div>

                      <Field label="Banner instructions">
                        <Textarea
                          rows={2}
                          className="align-top text-xs sm:text-sm font-sans"
                          placeholder="e.g. Read the text below and answer Questions 1-13."
                          value={part.instructions}
                          onChange={(e) => setPart('instructions', e.target.value)}
                        />
                      </Field>

                      <Field label="Material / Reading passage text">
                        <Textarea
                          rows={12}
                          className="align-top font-mono text-xs leading-relaxed"
                          placeholder="Paste or write the full passage, lecture transcript, or prompt material here..."
                          value={part.material}
                          onChange={(e) => setPart('material', e.target.value)}
                        />
                      </Field>
                    </CardContent>
                  </Card>

                  {/* Import, OCR, and AI Draft Assistant */}
                  <Card>
                    <CardHeader className="pb-3">
                      <CardTitle className="text-sm font-semibold flex items-center gap-2">
                        <Sparkles className="h-4 w-4 text-primary" /> Content Import & AI Generation
                      </CardTitle>
                      <CardDescription>
                        Import documents, diagrams, or use an AI model to draft structured exam questions from text.
                      </CardDescription>
                    </CardHeader>
                    <CardContent className="flex flex-col gap-4">
                      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                        <FileImport
                          label="Import Document (.txt, .pdf, .docx, .json)"
                          accept=".txt,.md,.json,.csv,.pdf,.docx"
                          disabled={page.busy}
                          onFiles={importIntoPart}
                        />
                        <FileImport
                          label="Read Pictures / Charts (.png, .jpg)"
                          accept=".png,.jpg,.jpeg"
                          disabled={page.busy || !d.canUseVision}
                          onFiles={readIntoPart}
                        />
                      </div>
                      {d.visionHint && (
                        <p className="text-xs text-muted-foreground">{d.visionHint}</p>
                      )}

                      <Separator />

                      <div className="space-y-3">
                        <Field label="Quick paste & AI Drafting">
                          <Textarea
                            rows={3}
                            className="align-top text-xs font-sans"
                            placeholder="Paste raw text or notes here, then draft questions automatically..."
                            value={d.pasteText}
                            onChange={(e) => setPaper('paste', e.target.value)}
                          />
                        </Field>

                        <div className="flex flex-wrap items-center gap-2">
                          <Select
                            value={d.pasteSkill}
                            onValueChange={(v) => setPaper('pasteSkill', v)}
                          >
                            <SelectTrigger className="w-[140px] h-8 text-xs">
                              <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                              {d.skills.map((s) => (
                                <SelectItem key={s} value={s}>{s}</SelectItem>
                              ))}
                            </SelectContent>
                          </Select>

                          <Button
                            variant="outline"
                            size="sm"
                            className="h-8 text-xs"
                            onClick={() => page.run('editor.buildPartFromPaste')}
                          >
                            Paste text into part
                          </Button>

                          <Button
                            variant="default"
                            size="sm"
                            className="h-8 text-xs gap-1.5"
                            disabled={!d.canUseAi || page.busy}
                            title={d.aiHint}
                            onClick={() => page.run('editor.aiDraft')}
                          >
                            <Sparkles className="h-3.5 w-3.5" />
                            {page.busy ? 'Drafting with AI...' : 'AI draft paper'}
                          </Button>

                          {page.busy && (
                            <Button
                              variant="destructive"
                              size="sm"
                              className="h-8 text-xs"
                              onClick={() => page.run('editor.cancelAi')}
                            >
                              Stop
                            </Button>
                          )}
                        </div>
                        {d.aiHint && <p className="text-xs text-muted-foreground">{d.aiHint}</p>}
                      </div>
                    </CardContent>
                  </Card>
                </>
              )}
            </div>
          </div>
        </TabsContent>

        {/* Tab 3: Question Builder */}
        <TabsContent value="questions" className="mt-4 flex flex-col gap-5">
          {!part ? (
            <Card className="flex h-72 items-center justify-center text-center p-6 text-muted-foreground">
              <div>
                <ListChecks className="h-8 w-8 mx-auto mb-2 opacity-50" />
                <p className="text-sm font-medium">Add or select a part in the &quot;Parts &amp; Materials&quot; tab first.</p>
              </div>
            </Card>
          ) : (
            <div className="grid grid-cols-1 gap-5 lg:grid-cols-12 items-start">
              {/* Left Column: Questions List for selected part */}
              <div className="lg:col-span-4 flex flex-col gap-3">
                <Card>
                  <CardHeader className="pb-3">
                    <div className="flex items-center justify-between">
                      <div>
                        <CardTitle className="text-sm font-semibold">Questions in {part.id}</CardTitle>
                        <CardDescription className="text-xs">{part.title || 'Untitled Part'}</CardDescription>
                      </div>
                      <Button
                        size="sm"
                        className="h-7 text-xs gap-1"
                        onClick={() => page.run('editor.addQuestion', { partIndex: d.selectedPartIndex })}
                      >
                        <Plus className="h-3 w-3" /> Add Q
                      </Button>
                    </div>
                  </CardHeader>
                  <CardContent className="p-2 pt-0">
                    <div className="flex flex-col gap-1.5 max-h-[620px] overflow-y-auto pr-1">
                      {(part.questions ?? []).length === 0 ? (
                        <div className="py-8 text-center text-xs text-muted-foreground">
                          No questions in this part yet. Click &quot;Add Q&quot; above.
                        </div>
                      ) : (
                        part.questions.map((q, i) => {
                          const isSelected = i === d.selectedQuestionIndex
                          return (
                            <div
                              key={i}
                              onClick={() => page.run('editor.selectQuestion', { index: i })}
                              className={`flex cursor-pointer items-start justify-between gap-2 rounded-lg border p-3 transition-colors ${
                                isSelected
                                  ? 'border-primary bg-primary/[0.08] shadow-xs'
                                  : 'hover:border-primary/30 hover:bg-muted/40'
                              }`}
                            >
                              <div className="min-w-0 flex-1 space-y-1">
                                <div className="flex items-center gap-1.5">
                                  <Badge variant="outline" className="text-[10px] font-bold px-1.5 py-0 h-4">
                                    Q{q.number}
                                  </Badge>
                                  <Badge variant="secondary" className="text-[10px] px-1 py-0 h-4">
                                    {q.kind}
                                  </Badge>
                                </div>
                                <p className="text-xs text-foreground/90 truncate leading-relaxed">
                                  {q.prompt || 'Empty prompt'}
                                </p>
                              </div>
                              <Button
                                size="icon"
                                variant="ghost"
                                className="h-6 w-6 text-muted-foreground hover:text-destructive shrink-0"
                                onClick={(e) => {
                                  e.stopPropagation()
                                  page.run('editor.removeQuestion', {
                                    partIndex: d.selectedPartIndex,
                                    questionIndex: i,
                                  })
                                }}
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </Button>
                            </div>
                          )
                        })
                      )}
                    </div>
                  </CardContent>
                </Card>
              </div>

              {/* Right Column: Selected Question Editor */}
              <div className="lg:col-span-8 flex flex-col gap-4">
                {!question ? (
                  <Card className="flex h-72 items-center justify-center text-center p-6 text-muted-foreground">
                    <div>
                      <ListChecks className="h-8 w-8 mx-auto mb-2 opacity-50" />
                      <p className="text-sm font-medium">Select a question on the left to edit its prompt, choices, and answers.</p>
                    </div>
                  </Card>
                ) : (
                  <Card>
                    <CardHeader className="pb-3">
                      <div className="flex items-center justify-between">
                        <CardTitle className="text-base font-semibold">
                          Question {question.number} Editor
                        </CardTitle>
                        <Badge variant="secondary">{question.kind}</Badge>
                      </div>
                    </CardHeader>
                    <CardContent className="flex flex-col gap-4">
                      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                        <Field label="Question number">
                          <Input
                            type="number"
                            min="1"
                            value={question.number}
                            onChange={(e) => setQuestion('number', e.target.value)}
                          />
                        </Field>
                        <Field label="Question kind">
                          <Select
                            value={question.kind}
                            onValueChange={(v) => setQuestion('kind', v)}
                          >
                            <SelectTrigger><SelectValue /></SelectTrigger>
                            <SelectContent>
                              {d.kinds.map((k) => (
                                <SelectItem key={k} value={k}>{k}</SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </Field>
                      </div>

                      <Field label="Question prompt">
                        <Textarea
                          rows={3}
                          className="align-top text-xs sm:text-sm"
                          placeholder="e.g. According to paragraph 2, why did early shipwrights choose oak?"
                          value={question.prompt}
                          onChange={(e) => setQuestion('prompt', e.target.value)}
                        />
                      </Field>

                      {/* Kind-specific fields */}
                      {question.kind === 'choice' && (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                          <Field label="Options (one per line, e.g. A. text)" className="sm:col-span-2">
                            <Textarea
                              rows={5}
                              className="align-top font-mono text-xs"
                              placeholder="A. First option&#10;B. Second option&#10;C. Third option&#10;D. Fourth option"
                              value={question.optionsText}
                              onChange={(e) => setQuestion('options', e.target.value)}
                            />
                          </Field>
                          <Field label="Correct key">
                            <Input
                              placeholder="e.g. A or B,C"
                              value={question.correctKey}
                              onChange={(e) => setQuestion('correctKey', e.target.value)}
                            />
                            <p className="mt-1 text-[11px] text-muted-foreground">
                              For multiple choices, separate keys with comma (e.g. B,D).
                            </p>
                          </Field>
                        </div>
                      )}

                      {question.kind === 'gap' && (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                          <Field label="Gap answer (use | for acceptable alternatives)">
                            <Input
                              placeholder="e.g. temperature | temperatures"
                              value={question.gapAnswer}
                              onChange={(e) => setQuestion('gapAnswer', e.target.value)}
                            />
                          </Field>
                          <Field label="Word bank (optional, one per line)">
                            <Textarea
                              rows={3}
                              className="align-top font-mono text-xs"
                              placeholder="word one&#10;word two&#10;word three"
                              value={question.bankText}
                              onChange={(e) => setQuestion('bank', e.target.value)}
                            />
                          </Field>
                        </div>
                      )}

                      {question.kind === 'match' && (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                          <Field label="Match rows (Label => Answer per line)">
                            <Textarea
                              rows={4}
                              className="align-top font-mono text-xs"
                              placeholder="Section A => Heading iii&#10;Section B => Heading v"
                              value={question.matchRowsText}
                              onChange={(e) => setQuestion('matchRows', e.target.value)}
                            />
                          </Field>
                          <Field label="Bank / Headings list (one per line)">
                            <Textarea
                              rows={4}
                              className="align-top font-mono text-xs"
                              placeholder="Heading i&#10;Heading ii&#10;Heading iii"
                              value={question.bankText}
                              onChange={(e) => setQuestion('bank', e.target.value)}
                            />
                          </Field>
                        </div>
                      )}

                      <Field label="Answer explanation (shown during test review)">
                        <Textarea
                          rows={2}
                          className="align-top text-xs font-sans"
                          placeholder="Explain why this answer is correct based on the passage or audio..."
                          value={question.explanation}
                          onChange={(e) => setQuestion('explanation', e.target.value)}
                        />
                      </Field>
                    </CardContent>
                  </Card>
                )}
              </div>
            </div>
          )}
        </TabsContent>
      </Tabs>

      {d.statusMessage && (
        <div className="rounded-md border border-border bg-card px-4 py-2.5 text-xs text-muted-foreground">
          {d.statusMessage}
        </div>
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

