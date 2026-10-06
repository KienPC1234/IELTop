import { useState } from 'react'
import { MessageSquare, Mic, AlertCircle } from 'lucide-react'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ErrorBar, PageHeader } from '@/components/shared'
import StudyChatTutor from '@/pages/study/StudyChatTutor'
import StudySpeakingCoach from '@/pages/study/StudySpeakingCoach'

/// The renovated Study screen: divided cleanly into two interactive tabs:
/// 1. AI Study Tutor: an interactive chatbot grounded in the complete 14-unit lesson
///    curriculum, capable of explaining concepts and generating 8 interactive exercise types.
/// 2. Speaking Coach: an intelligent speaking partner with flexible topic suggestions,
///    official 4-criteria IELTS evaluation (FC, LR, GRA, PR), and transcript-aligned audio slicing.
export default function Study({ onNavigate }: { onNavigate?: (target: string) => void }) {
  const [tab, setTab] = useState<'tutor' | 'speaking'>('tutor')
  const [error, setError] = useState('')

  return (
    <div className="flex flex-col gap-4">
      <ErrorBar message={error} onDismiss={() => setError('')} />

      <PageHeader
        title="Study & Speaking"
        description="Interactive IELTS AI tutor grounded in authentic curriculum, and Speaking coach with 4-criteria assessment and audio segment drills."
      />

      <Tabs value={tab} onValueChange={(val: any) => setTab(val)}>
        <TabsList className="grid w-full max-w-[400px] grid-cols-2">
          <TabsTrigger value="tutor" className="gap-2">
            <MessageSquare className="h-4 w-4" />
            AI Study Tutor
          </TabsTrigger>
          <TabsTrigger value="speaking" className="gap-2">
            <Mic className="h-4 w-4" />
            Speaking Coach
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {tab === 'tutor' && <StudyChatTutor onError={setError} />}
      {tab === 'speaking' && <StudySpeakingCoach onError={setError} />}
    </div>
  )
}
