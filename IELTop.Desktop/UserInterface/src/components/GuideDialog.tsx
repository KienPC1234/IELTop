import { useState } from 'react'
import { HelpCircle, ChevronDown, MessageSquare, Layers, Mic, BookText, FileText, Library, BarChart3, Server, Settings as SettingsIcon, PlugZap } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger,
} from '@/components/ui/dialog'
import { cn } from '@/lib/utils'

/// One guide section: a title with an icon and numbered steps.
const SECTIONS: { icon: any, title: string, steps: string[] }[] = [
  {
    icon: PlugZap,
    title: 'Start in one minute',
    steps: [
      'Pick Mock Test in the sidebar, choose a paper, press Start. Answer, then Submit to see your band estimates.',
      'Open Study, press Speaking, then New, pick a cue, test the microphone, and Record. Stop to get your transcript and scores.',
      'Open Settings, add a language model to unlock the tutor chat, AI practice builder, and detailed speaking feedback.',
    ],
  },
  {
    icon: FileText,
    title: 'Mock Test',
    steps: [
      'Pick a paper and the skills to run, then Start. The clock counts down and moves you on when time runs out.',
      'Listening plays once and Reading, Writing, Speaking each get their own panel. Flag questions to revisit them in review.',
      'Submit to see every answer with the correct key. Writing and Speaking need AI marking or a teacher for bands.',
      'All bands are practice estimates, never official IELTS scores.',
    ],
  },
  {
    icon: Library,
    title: 'Paper Library and Editor',
    steps: [
      'Use opens a paper in Mock Test. Add to basket collects papers into one custom test, then Start custom test.',
      'Edit opens the paper in the Editor tab: change parts, questions, and keys, then Save. Duplicate keeps the original safe.',
      'AI check reviews a paper for mistakes. It needs a language model.',
      'Import files or paste text to draft a paper. Text imports work offline; AI draft needs a language model.',
    ],
  },
  {
    icon: MessageSquare,
    title: 'Study: tutor chat',
    steps: [
      'Type a question and press Send. The tutor answers from your lesson units and shows the source under each reply.',
      'Type a word and press Look up, Translate, or Fix English to run that tool on your text.',
      'Limit to a unit narrows answers to one unit. Sessions on the left keep every thread; pin the ones you revisit.',
    ],
  },
  {
    icon: Layers,
    title: 'Study: practice sets',
    steps: [
      'Choose a unit, skill, and difficulty, then press Quick Quiz for instant offline questions from your lessons.',
      'Generate with AI writes fresh questions and needs a language model. Answer in the set, then press Score set.',
      'Wrong answers show the correct key. Press Explain on any scored question for an AI explanation.',
      'Saved sets stay on the right for review. They are yours: rename, pin, or delete them any time.',
    ],
  },
  {
    icon: Mic,
    title: 'Study: speaking tutor',
    steps: [
      'Pick Part 1, 2, 3, or Read aloud, then press New and choose a cue. Part 2 gives you 60 seconds to prepare.',
      'Press Test microphone once, then Record and speak. Press Stop to score: transcript, pace, pauses, and sound scores.',
      'Read aloud needs only the pronunciation model. Answering cues also needs the transcription model.',
      'Press Get feedback for band estimates and tips. It needs a language model; every band is a practice estimate.',
    ],
  },
  {
    icon: BookText,
    title: 'Study: materials and vocabulary',
    steps: [
      'Materials lists every lesson unit with its sections, slides, and audio. Everything reads offline.',
      'Vocabulary searches all 1700 plus words by English or Vietnamese, with pronunciation and word families.',
    ],
  },
  {
    icon: BarChart3,
    title: 'Score History',
    steps: [
      'Every submitted test and tutor attempt lands here with its bands and date, so progress is visible over weeks.',
      'Filter by skill to see one curve at a time. Delete old records you no longer need.',
    ],
  },
  {
    icon: Server,
    title: 'Community Hub',
    steps: [
      'Pick a server and press Connect to list shared papers, then Download to add them to your library.',
      'Add your own server with Name and Address. Downloaded papers run in Mock Test like built in ones.',
    ],
  },
  {
    icon: SettingsIcon,
    title: 'Settings: AI, audio, models',
    steps: [
      'AI and Models holds any OpenAI compatible chat API: base URL, model name, and key. The key is stored encrypted on this machine.',
      'Press Test connection before saving. Save keeps the settings; the test never writes to disk.',
      'Sound and Mic picks your speaker and microphone and plays a test tone. The first microphone test also unlocks device names.',
      'Offline Models lists every speech model with its source and license, and whether its files are installed.',
      'Missing model files never crash the app. The feature that needs them explains itself and stays disabled.',
    ],
  },
]

/// The in app guide: every feature in numbered steps, no jargon. Static text,
/// so it opens instantly and works with no model and no network.
export default function GuideDialog() {
  const [open, setOpen] = useState<string | null>('Start in one minute')

  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="ghost" size="icon" className="h-8 w-8" title="How to use the app">
          <HelpCircle className="h-4 w-4" />
        </Button>
      </DialogTrigger>
      <DialogContent className="max-h-[85vh] max-w-2xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>How to use IELTop</DialogTitle>
          <DialogDescription>
            Every feature in a few steps. Nothing here needs an account, and most of it works offline.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-2">
          {SECTIONS.map((s) => {
            const Icon = s.icon
            const expanded = open === s.title
            return (
              <div key={s.title} className="rounded-lg border border-border">
                <button
                  type="button"
                  onClick={() => setOpen(expanded ? null : s.title)}
                  className="flex w-full items-center gap-2.5 px-3.5 py-2.5 text-left text-sm font-semibold"
                  aria-expanded={expanded}
                >
                  <Icon className="h-4 w-4 shrink-0 text-primary" />
                  <span className="flex-1">{s.title}</span>
                  <ChevronDown className={cn('h-4 w-4 text-muted-foreground transition-transform', expanded && 'rotate-180')} />
                </button>
                {expanded && (
                  <ol className="flex list-decimal flex-col gap-1.5 px-3.5 pb-3.5 pl-9">
                    {s.steps.map((step, i) => (
                      <li key={i} className="text-sm leading-relaxed text-muted-foreground">{step}</li>
                    ))}
                  </ol>
                )}
              </div>
            )
          })}
        </div>
      </DialogContent>
    </Dialog>
  )
}
