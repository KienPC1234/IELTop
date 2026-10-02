import {
  Award,
  BookOpen,
  Brain,
  CheckCircle2,
  ChevronRight,
  Clock,
  Compass,
  FileText,
  Flame,
  Headphones,
  Mic,
  PenTool,
  Play,
  RefreshCw,
  Sparkles,
  TrendingUp,
  Zap,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { EmptyState } from '@/components/shared'
import ActivityBars from '@/components/ActivityBars'
import BandTrend from '@/components/BandTrend'
import { cn } from '@/lib/utils'

interface OverviewProps {
  data: any
  loading: boolean
  error: string
  onRefresh: () => void
  onNavigate?: (page: string) => void
}

export default function Overview({ data, loading, error, onRefresh, onNavigate }: OverviewProps) {
  if (loading && !data) {
    return (
      <div className="flex flex-col gap-6">
        <Skeleton className="h-44 w-full rounded-2xl" />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-32 rounded-xl" />
          ))}
        </div>
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <Skeleton className="h-72 rounded-xl" />
          <Skeleton className="h-72 rounded-xl" />
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="rounded-2xl border border-destructive/30 bg-destructive/10 p-6 text-sm text-destructive shadow-xs">
        <h3 className="text-base font-semibold">Unable to load dashboard data</h3>
        <p className="mt-1 text-muted-foreground">{error}</p>
        <Button variant="outline" size="sm" onClick={onRefresh} className="mt-4 gap-2">
          <RefreshCw className="h-4 w-4" /> Try again
        </Button>
      </div>
    )
  }

  if (!data) return null

  const hasActivity = data.weeklyActivity?.some((d: any) => d.count > 0)
  const hasTrend = (data.bandTrend?.length ?? 0) >= 2

  const SKILLS = [
    {
      id: 'listening',
      name: 'Listening',
      desc: '4 sections, 40 questions with native audio tracks and timed replay prevention.',
      detail: '40 questions • 30 mins',
      icon: Headphones,
      badge: 'Native Audio',
      colorBg: 'bg-blue-500/10 text-blue-600 dark:text-blue-400',
      borderHover: 'hover:border-blue-500/40',
    },
    {
      id: 'reading',
      name: 'Reading',
      desc: '3 authentic academic passages with draggable split pane and gap-fill markers.',
      detail: '40 questions • 60 mins',
      icon: BookOpen,
      badge: 'Split Screen',
      colorBg: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400',
      borderHover: 'hover:border-emerald-500/40',
    },
    {
      id: 'writing',
      name: 'Writing',
      desc: 'Task 1 visual summaries and Task 2 opinion essays with live word counting.',
      detail: '2 tasks • 60 mins',
      icon: PenTool,
      badge: 'AI Rubric',
      colorBg: 'bg-amber-500/10 text-amber-600 dark:text-amber-400',
      borderHover: 'hover:border-amber-500/40',
    },
    {
      id: 'speaking',
      name: 'Speaking',
      desc: '3-part interactive interview drills analyzed with on-device phoneme models.',
      detail: '3 parts • 14 mins',
      icon: Mic,
      badge: 'Phoneme Model',
      colorBg: 'bg-rose-500/10 text-rose-600 dark:text-rose-400',
      borderHover: 'hover:border-rose-500/40',
    },
  ]

  return (
    <div className="flex flex-col gap-8 pb-10">
      {/* Hero Welcome Banner */}
      <div className="relative overflow-hidden rounded-2xl border border-primary/20 bg-gradient-to-br from-primary/10 via-card to-card p-7 shadow-xs">
        <div className="relative z-10 flex flex-col justify-between gap-6 md:flex-row md:items-center">
          <div className="max-w-xl space-y-2.5">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="secondary" className="gap-1.5 px-2.5 py-0.5 text-xs font-bold uppercase tracking-wider text-primary">
                <Sparkles className="h-3.5 w-3.5" />
                <span>Offline IELTS Simulator</span>
              </Badge>
              {data.activeToday && (
                <Badge variant="outline" className="gap-1.5 border-emerald-500/40 bg-emerald-500/10 text-xs font-semibold text-emerald-600 dark:text-emerald-400">
                  <CheckCircle2 className="h-3.5 w-3.5" />
                  <span>Session finished today</span>
                </Badge>
              )}
            </div>
            <h2 className="text-2xl font-extrabold tracking-tight text-foreground sm:text-3xl">
              Master IELTS with precision
            </h2>
            <p className="text-sm leading-relaxed text-muted-foreground">
              Practice all four skills completely offline on your computer. Band estimates are calibrated to official public assessment criteria.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Button
              size="lg"
              className="gap-2 font-semibold shadow-xs transition-all hover:shadow"
              onClick={() => onNavigate?.('mock')}
            >
              <Play className="h-4 w-4 fill-current" />
              <span>Start Full Test</span>
            </Button>
            <Button
              variant="outline"
              size="lg"
              className="gap-2 font-medium"
              onClick={() => onNavigate?.('library')}
            >
              <BookOpen className="h-4 w-4" />
              <span>Browse Papers</span>
            </Button>
          </div>
        </div>
      </div>

      {/* 4 Metric Stats Cards */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {/* Study Streak */}
        <Card className="overflow-hidden transition-all duration-150 hover:border-primary/40 hover:shadow-2xs">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
              Study Streak
            </CardTitle>
            <div className={cn(
              'flex h-9 w-9 items-center justify-center rounded-xl',
              data.streakDays > 0 ? 'bg-amber-500/15 text-amber-500' : 'bg-muted text-muted-foreground'
            )}>
              <Flame className="h-5 w-5 fill-current" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-black tracking-tight tabular-nums">
              {data.streakDays} <span className="text-sm font-normal text-muted-foreground">days</span>
            </div>
            <p className="mt-1.5 text-xs text-muted-foreground">
              {data.streakHint || 'Finish a practice session to start a streak'}
            </p>
          </CardContent>
        </Card>

        {/* Estimated Band */}
        <Card className="overflow-hidden transition-all duration-150 hover:border-primary/40 hover:shadow-2xs">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
              Overall Band
            </CardTitle>
            <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Award className="h-5 w-5" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-black tracking-tight text-primary tabular-nums">
              {data.lastBandLabel || 'Band --'}
            </div>
            <p className="mt-1.5 text-xs text-muted-foreground">
              {data.examAttempts > 0 ? `Estimated from ${data.examAttempts} scored session(s)` : 'Complete a test to get a band estimate'}
            </p>
          </CardContent>
        </Card>

        {/* Tests Completed */}
        <Card className="overflow-hidden transition-all duration-150 hover:border-primary/40 hover:shadow-2xs">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
              Completed Tests
            </CardTitle>
            <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-emerald-500/10 text-emerald-500">
              <TrendingUp className="h-5 w-5" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-black tracking-tight tabular-nums">
              {data.examAttempts} <span className="text-sm font-normal text-muted-foreground">sessions</span>
            </div>
            <p className="mt-1.5 text-xs text-muted-foreground">
              Saved locally in SQLite database
            </p>
          </CardContent>
        </Card>

        {/* Local AI Status */}
        <Card className="overflow-hidden transition-all duration-150 hover:border-primary/40 hover:shadow-2xs">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
              AI & Acoustic Models
            </CardTitle>
            <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-purple-500/10 text-purple-500">
              <Brain className="h-5 w-5" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <span className="text-2xl font-black tracking-tight">
                {data.llmConfigured ? 'Ready' : 'Offline'}
              </span>
              <span className={cn(
                'inline-flex h-2.5 w-2.5 rounded-full',
                data.llmConfigured ? 'bg-emerald-500' : 'bg-muted-foreground/40'
              )} />
            </div>
            <p className="mt-1.5 truncate text-xs text-muted-foreground" title={data.llmSummary}>
              {data.modelsReady} of {data.modelsTotal} ONNX models ready
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Skills Practice Launchpad */}
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="text-lg font-bold tracking-tight text-foreground">Skills Practice Launchpad</h3>
            <p className="text-xs text-muted-foreground">Choose a specific module to focus your daily revision</p>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onNavigate?.('mock')}
            className="text-xs font-medium text-primary hover:text-primary"
          >
            <span>Custom test setup</span>
            <ChevronRight className="ml-1 h-3.5 w-3.5" />
          </Button>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {SKILLS.map((skill) => {
            const Icon = skill.icon
            return (
              <div
                key={skill.id}
                className={cn(
                  'group flex flex-col justify-between rounded-xl border border-border bg-card p-5 transition-all duration-150 hover:shadow-xs',
                  skill.borderHover
                )}
              >
                <div>
                  <div className="flex items-start justify-between gap-2">
                    <div className={cn('flex h-10 w-10 items-center justify-center rounded-xl', skill.colorBg)}>
                      <Icon className="h-5 w-5" />
                    </div>
                    <Badge variant="secondary" className="text-[10px] font-semibold tracking-wide">
                      {skill.badge}
                    </Badge>
                  </div>

                  <div className="mt-4">
                    <h4 className="text-base font-bold text-foreground group-hover:text-primary transition-colors">
                      {skill.name}
                    </h4>
                    <p className="mt-1 text-xs text-muted-foreground/80 font-mono">
                      {skill.detail}
                    </p>
                    <p className="mt-2 text-xs leading-relaxed text-muted-foreground">
                      {skill.desc}
                    </p>
                  </div>
                </div>

                <div className="mt-5 pt-3 border-t border-border/50">
                  <Button
                    variant="outline"
                    size="sm"
                    className="w-full justify-between text-xs font-semibold group-hover:border-primary group-hover:text-primary"
                    onClick={() => onNavigate?.('mock')}
                  >
                    <span>Practice {skill.name}</span>
                    <ChevronRight className="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>
            )
          })}
        </div>
      </div>

      {/* Analytics Grid: Activity & Band Progression */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Weekly Activity */}
        <Card className="transition-all hover:border-border/80">
          <CardHeader className="flex flex-row items-center justify-between pb-3">
            <div>
              <CardTitle className="text-base font-bold">Weekly Activity</CardTitle>
              <CardDescription className="text-xs">Practice sessions logged over the last 7 days</CardDescription>
            </div>
            <Button variant="ghost" size="icon" onClick={onRefresh} aria-label="Refresh activity" className="h-8 w-8">
              <RefreshCw className="h-4 w-4" />
            </Button>
          </CardHeader>
          <CardContent className="pt-2">
            {hasActivity ? (
              <ActivityBars days={data.weeklyActivity} />
            ) : (
              <EmptyState
                title="No activity recorded"
                body="Complete a timed mock test or a speaking drill to populate your activity chart."
              />
            )}
          </CardContent>
        </Card>

        {/* Band Progression */}
        <Card className="transition-all hover:border-border/80">
          <CardHeader className="flex flex-row items-center justify-between pb-3">
            <div>
              <CardTitle className="text-base font-bold">Band Score Progression</CardTitle>
              <CardDescription className="text-xs">Historical practice performance calibrated to Band 0 - 9.0</CardDescription>
            </div>
            <Badge variant="outline" className="text-xs font-semibold">
              Official Scale
            </Badge>
          </CardHeader>
          <CardContent className="pt-2">
            {hasTrend ? (
              <BandTrend points={data.bandTrend} />
            ) : (
              <EmptyState
                title="Not enough score data"
                body="Complete at least two full mock tests to graph your performance trajectory over time."
              />
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
