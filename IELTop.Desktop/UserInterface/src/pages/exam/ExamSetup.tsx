import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ErrorBar } from '@/components/shared'
import { call } from '@/bridge'

/// The setup screen: pick a paper, tick skills and task types, then start.
export default function ExamSetup({ setup, onApply, onStart }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

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
