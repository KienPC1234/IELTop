import { useEffect, useState } from 'react'
import { call } from '@/bridge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ErrorBar, PageHeader } from '@/components/shared'
import { MessageSquare, Layers, BookText } from 'lucide-react'
import StudyChat from '@/pages/study/StudyChat'
import StudyPractice from '@/pages/study/StudyPractice'
import StudyVocab from '@/pages/study/StudyVocab'

/// The Study screen: a tutor chat grounded in the lesson content, a practice
/// builder, and a vocabulary browser. Reading and browsing work offline; the
/// chat and the builder need a language model and say so when none is set.
export default function Study({ onNavigate }) {
  const [tab, setTab] = useState('chat')
  const [units, setUnits] = useState<any[]>([])
  const [canUseAi, setCanUseAi] = useState(false)
  const [aiHint, setAiHint] = useState('')
  const [hasUnits, setHasUnits] = useState(true)
  const [status, setStatus] = useState('')
  const [error, setError] = useState('')

  async function loadShell() {
    try {
      const snap = await call('study.chat.snapshot', { sessionId: 0, unit: '' })
      setUnits(snap?.units ?? [])
      setCanUseAi(!!snap?.canUseAi)
      setAiHint(snap?.aiHint ?? '')
      setHasUnits(snap?.hasUnits ?? true)
      if (snap?.statusMessage) setStatus(snap.statusMessage)
    } catch (e: any) {
      setError(e.message)
    }
  }

  useEffect(() => {
    loadShell()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div className="flex flex-col gap-5">
      <ErrorBar message={error} onDismiss={() => setError('')} />

      <PageHeader
        title="Study"
        description="A tutor chat, practice sets, and the course vocabulary. Bands are practice estimates only."
      />

      {!hasUnits && (
        <Card>
          <CardContent>
            <p className="text-sm leading-relaxed text-muted-foreground">
              No lesson content was found. Run the lesson ingest tool once to add the
              course units, then reload this screen.
            </p>
          </CardContent>
        </Card>
      )}

      {!canUseAi && (
        <div className="flex flex-wrap items-center gap-3 rounded-lg border border-warning/30 bg-warning/10 px-3.5 py-2.5 text-sm text-warning">
          <span>{aiHint}</span>
          <Button size="sm" variant="outline" className="h-7 text-xs" onClick={() => onNavigate?.('settings')}>
            Open Settings
          </Button>
        </div>
      )}

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="chat" className="gap-1.5">
            <MessageSquare className="h-4 w-4" />
            Tutor chat
          </TabsTrigger>
          <TabsTrigger value="practice" className="gap-1.5">
            <Layers className="h-4 w-4" />
            Practice
          </TabsTrigger>
          <TabsTrigger value="vocab" className="gap-1.5">
            <BookText className="h-4 w-4" />
            Vocabulary
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {tab === 'chat' && <StudyChat units={units} canUseAi={canUseAi} onError={setError} />}
      {tab === 'practice' && <StudyPractice units={units} canUseAi={canUseAi} onError={setError} />}
      {tab === 'vocab' && <StudyVocab units={units} onError={setError} />}
    </div>
  )
}
