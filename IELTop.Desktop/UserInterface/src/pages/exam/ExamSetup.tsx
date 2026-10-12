import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ErrorBar } from '@/components/shared'
import { call } from '@/bridge'
import { listDevices, recordAndPlayback } from '@/audio'
import { Mic, CheckCircle2, AlertCircle, RefreshCw } from 'lucide-react'

/// The setup screen: pick a paper, tick skills and task types, then start.
export default function ExamSetup({ setup, onApply, onStart }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [micList, setMicList] = useState<{ id: string; label: string }[]>([])
  const [selectedMic, setSelectedMic] = useState<string>('__default')
  const [micTestState, setMicTestState] = useState<'idle' | 'testing' | 'ok' | 'fail'>('idle')
  const [micTestError, setMicTestError] = useState('')

  useEffect(() => {
    loadMics()
  }, [])

  async function loadMics() {
    try {
      const dev = await listDevices()
      setMicList(dev.mics || [])
      const s: any = await call('settings.snapshot')
      if (s?.audioInputDeviceId) {
        setSelectedMic(s.audioInputDeviceId)
      }
    } catch {
      // fallback
    }
  }

  async function handleSelectMic(devId: string) {
    setSelectedMic(devId)
    const val = devId === '__default' ? '' : devId
    try {
      await call('settings.setAudioInput', { value: val })
    } catch {
      // fallback
    }
  }

  async function handleTestMic() {
    setMicTestState('testing')
    setMicTestError('')
    try {
      const dev = selectedMic === '__default' ? undefined : selectedMic
      const blob = await recordAndPlayback(dev, 2.5)
      if (blob && blob.size > 0) {
        setMicTestState('ok')
        const updated = await listDevices()
        if (updated.mics.length > 0) setMicList(updated.mics)
      } else {
        setMicTestState('fail')
        setMicTestError('No sound was detected. Please check your microphone.')
      }
    } catch {
      setMicTestState('fail')
      setMicTestError('Microphone access denied or device unavailable.')
    }
  }

  const includesSpeaking = (setup.skills ?? []).some((s: any) => s.isSelected && s.name.toLowerCase() === 'speaking')

  async function run(method, args) {
    setBusy(true)
    setError('')
    try {
      onApply(await call(method, args))
    } catch (e) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  if (!setup.hasPapers) {
    return (
      <Card>
        <CardContent>
          <p className="text-sm leading-relaxed text-muted-foreground">{setup.materialWarning}</p>
        </CardContent>
      </Card>
    )
  }

  return (
    <div className="flex flex-col gap-5">
      <ErrorBar message={error} onDismiss={() => setError('')} />
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Build your test</CardTitle>
          <CardDescription>Pick a paper, then choose the skills to run.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label>Test paper</Label>
            <Select
              value={setup.selectedPaperTitle}
              disabled={setup.mixAllPapers}
              onValueChange={(v) => run('exam.selectPaper', { title: v })}
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                {(setup.papers ?? []).map((p) => (
                  <SelectItem key={p.title} value={p.title}>
                    {p.title} ({p.category || 'General'}, {p.level || 'any level'})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <label className="flex cursor-pointer items-center gap-2.5 text-sm leading-relaxed">
            <Switch checked={setup.mixAllPapers} onCheckedChange={(v) => run('exam.setMixAllPapers', { value: v })} />
            Mix all papers
          </label>
          <label className="flex cursor-pointer items-center gap-2.5 text-sm leading-relaxed">
            <Switch checked={setup.shuffleParts} onCheckedChange={(v) => run('exam.setShuffleParts', { value: v })} />
            Shuffle the part order
          </label>

          <div className="flex flex-col gap-2">
            <Label>Skills</Label>
            <div className="flex flex-wrap gap-2">
              {(setup.skills ?? []).map((s) => (
                <Button
                  key={s.name}
                  type="button"
                  size="sm"
                  variant={s.isSelected ? 'default' : 'outline'}
                  disabled={s.partCount === 0 || busy}
                  title={s.partCount === 0 ? 'No parts in this paper' : `${s.partCount} part(s)`}
                  onClick={() => run('exam.toggleSkill', { name: s.name })}
                >
                  {s.name}
                  <span className="ml-1.5 text-xs opacity-70">{s.partCount > 0 ? s.partCount : 'none'}</span>
                </Button>
              ))}
            </div>
            <p className="text-sm leading-relaxed text-muted-foreground">{setup.selectedSkillsLabel}</p>
          </div>

          {(setup.taskTypes ?? []).length > 0 && (
            <div className="flex flex-col gap-2">
              <Label>Task types</Label>
              <div className="flex flex-wrap gap-2">
                {(setup.taskTypes ?? []).map((t) => (
                  <Button
                    key={t.name}
                    type="button"
                    size="sm"
                    variant={t.isSelected ? 'default' : 'outline'}
                    onClick={() => run('exam.toggleTaskType', { name: t.name })}
                  >
                    {t.name}
                  </Button>
                ))}
              </div>
              <p className="text-sm leading-relaxed text-muted-foreground">{setup.selectedTaskTypesLabel}</p>
            </div>
          )}
        </CardContent>
      </Card>

      {includesSpeaking && (
        <Card className="border-primary/30 bg-card">
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle className="text-base flex items-center gap-2">
                  <Mic className="h-4 w-4 text-primary" />
                  Microphone Check (Speaking Exam)
                </CardTitle>
                <CardDescription>
                  Select and verify your microphone before beginning the Speaking test.
                </CardDescription>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={loadMics}
                title="Refresh microphone devices"
                className="h-8 gap-1 text-xs"
              >
                <RefreshCw className="h-3.5 w-3.5" />
                Refresh
              </Button>
            </div>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <Label className="text-xs">Selected Microphone</Label>
              <Select value={selectedMic} onValueChange={handleSelectMic}>
                <SelectTrigger className="text-xs h-9">
                  <SelectValue placeholder="Select microphone" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__default">System default microphone</SelectItem>
                  {micList.map((m) => (
                    <SelectItem key={m.id} value={m.id}>
                      {m.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-wrap items-center gap-3 pt-1">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={micTestState === 'testing' || busy}
                onClick={handleTestMic}
                className="gap-1.5 text-xs font-semibold"
              >
                <Mic className="h-3.5 w-3.5" />
                {micTestState === 'testing' ? 'Listening & playing back (2.5s)...' : 'Test Microphone'}
              </Button>

              {micTestState === 'ok' && (
                <span className="flex items-center gap-1.5 text-xs font-medium text-emerald-600 dark:text-emerald-400">
                  <CheckCircle2 className="h-4 w-4" /> Microphone working properly.
                </span>
              )}
              {micTestState === 'fail' && (
                <span className="flex items-center gap-1.5 text-xs font-medium text-destructive">
                  <AlertCircle className="h-4 w-4" /> {micTestError}
                </span>
              )}
              {micTestState === 'idle' && (
                <span className="text-xs text-muted-foreground">
                  Click to speak a few words and verify playback sound.
                </span>
              )}
            </div>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">How it is marked</CardTitle>
          <CardDescription>Strictness only changes scoring, never the questions.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label>Marking level</Label>
            <div className="flex flex-wrap gap-2">
              {(setup.strictnessOptions ?? []).map((s) => (
                <Button key={s} type="button" size="sm" variant={setup.strictness === s ? 'default' : 'outline'} onClick={() => run('exam.setStrictness', { value: s })}>
                  {s}
                </Button>
              ))}
            </div>
          </div>
          <div className="flex flex-col gap-2">
            <Label>Part order</Label>
            <div className="flex flex-wrap gap-2">
              {(setup.buildModes ?? []).map((m) => {
                const noAi = m === 'AI pick' && !setup.canUseAi
                return (
                  <Button
                    key={m}
                    type="button"
                    size="sm"
                    variant={setup.buildMode === m ? 'default' : 'outline'}
                    disabled={noAi || busy}
                    title={noAi ? setup.aiHint : ''}
                    onClick={() => run('exam.setBuildMode', { value: m })}
                  >
                    {m}
                  </Button>
                )
              })}
            </div>
            {!setup.canUseAi && <p className="text-sm leading-relaxed text-muted-foreground">{setup.aiHint}</p>}
          </div>
          <div className="flex flex-col gap-2">
            <Label>Strict mode</Label>
            <div className="flex flex-wrap gap-2">
              {(setup.strictLevelOptions ?? []).map((s) => (
                <Button
                  key={s}
                  type="button"
                  size="sm"
                  variant={setup.strictLevel === s ? 'default' : 'outline'}
                  disabled={busy}
                  onClick={() => run('exam.setStrictLevel', { value: s })}
                >
                  {s}
                </Button>
              ))}
            </div>
            <p className="text-sm leading-relaxed text-muted-foreground">{setup.strictHint}</p>
          </div>
          <p className="text-sm leading-relaxed text-muted-foreground">{setup.paperCountLabel}</p>
        </CardContent>
      </Card>

      <div className="flex flex-wrap items-center gap-3 pt-2">
        <Button size="lg" disabled={!setup.canStart || busy} onClick={() => onStart?.()} className="min-w-36 font-semibold">
          {busy ? 'Working...' : 'Start test'}
        </Button>
        {!setup.canStart && (
          <span className="text-sm leading-relaxed text-muted-foreground">
            Select at least one skill with questions to start the test.
          </span>
        )}
      </div>
    </div>
  )
}
