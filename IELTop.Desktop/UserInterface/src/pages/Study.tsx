import { useEffect, useState } from 'react'
import { MessageSquare, Mic, BookText, Layers } from 'lucide-react'
import { call } from '@/bridge'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ErrorBar, PageHeader } from '@/components/shared'
import StudyChatTutor from '@/pages/study/StudyChatTutor'
import StudySpeakingCoach from '@/pages/study/StudySpeakingCoach'
import StudyVocab from '@/pages/study/StudyVocab'
import StudyPractice from '@/pages/study/StudyPractice'

/// Study screen: interactive AI Study Tutor & Speaking Coach paired with
/// offline Vocabulary browser and Practice question sets.
export default function Study({ onNavigate: _onNavigate }: { onNavigate?: (target: string) => void }) {
  const [tab, setTab] = useState<'tutor' | 'speaking' | 'vocab' | 'practice'>('tutor')
  const [units, setUnits] = useState<any[]>([])
  const [canUseAi, setCanUseAi] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    call('study.chat.snapshot', { sessionId: 0, unit: '' })
      .then((snap: any) => {
        setUnits(snap?.units ?? [])
        setCanUseAi(Boolean(snap?.canUseAi))
      })
      .catch((e: any) => setError(e.message))
  }, [])

  return (
    <div className="flex flex-col gap-4">
      <ErrorBar message={error} onDismiss={() => setError('')} />

      <PageHeader
        title="Study & Curriculum"
        description="Interactive AI tutor grounded in 14 curriculum units, Speaking coach with 4-criteria evaluation, academic vocabulary browser, and practice sets."
      />

      <Tabs value={tab} onValueChange={(val: any) => setTab(val)}>
        <TabsList className="grid w-full max-w-[640px] grid-cols-4">
          <TabsTrigger value="tutor" className="gap-2">
            <MessageSquare className="h-4 w-4" />
            AI Tutor
          </TabsTrigger>
          <TabsTrigger value="speaking" className="gap-2">
            <Mic className="h-4 w-4" />
            Speaking Coach
          </TabsTrigger>
          <TabsTrigger value="vocab" className="gap-2">
            <BookText className="h-4 w-4" />
            Vocabulary
          </TabsTrigger>
          <TabsTrigger value="practice" className="gap-2">
            <Layers className="h-4 w-4" />
            Practice Sets
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {tab === 'tutor' && <StudyChatTutor onError={setError} />}
      {tab === 'speaking' && <StudySpeakingCoach onError={setError} />}
      {tab === 'vocab' && <StudyVocab units={units} onError={setError} />}
      {tab === 'practice' && <StudyPractice units={units} canUseAi={canUseAi} onError={setError} />}
    </div>
  )
}
