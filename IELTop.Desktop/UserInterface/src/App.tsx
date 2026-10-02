import { useEffect, useState } from 'react'
import {
  BarChart3,
  BookOpen,
  CheckCircle2,
  ChevronRight,
  Clock,
  FileText,
  Flame,
  LayoutDashboard,
  PencilLine,
  Play,
  Server,
  Settings as SettingsIcon,
  Sparkles,
  Zap,
} from 'lucide-react'
import { call } from '@/bridge'
import { Button } from '@/components/ui/button'
import { ScrollArea } from '@/components/ui/scroll-area'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import Overview from '@/pages/Overview'
import MockTest from '@/pages/MockTest'
import Library from '@/pages/Library'
import Editor from '@/pages/Editor'
import Results from '@/pages/Results'
import Servers from '@/pages/Servers'
import Settings from '@/pages/Settings'

interface NavItem {
  key: string
  label: string
  Icon: React.ComponentType<{ className?: string }>
  desc: string
  tag?: string
}

interface NavGroup {
  title: string
  items: NavItem[]
}

const NAV_GROUPS: NavGroup[] = [
  {
    title: 'EXAM & PRACTICE',
    items: [
      { key: 'overview', label: 'Dashboard', Icon: LayoutDashboard, desc: 'Progress, streaks, and analytics' },
      { key: 'mock', label: 'Mock Test', Icon: FileText, desc: 'Timed simulated exam engine', tag: 'Timed' },
      { key: 'library', label: 'Paper Library', Icon: BookOpen, desc: 'Practice tests and materials' },
    ],
  },
  {
    title: 'AUTHORING & SYSTEM',
    items: [
      { key: 'editor', label: 'Exam Editor', Icon: PencilLine, desc: 'Build and modify exam papers' },
      { key: 'results', label: 'Score History', Icon: BarChart3, desc: 'Attempt records and band progression' },
      { key: 'servers', label: 'Community Hub', Icon: Server, desc: 'Download community test papers' },
      { key: 'settings', label: 'Settings', Icon: SettingsIcon, desc: 'Audio devices, AI models, and preferences' },
    ],
  },
]

export default function App() {
  const [page, setPage] = useState('overview')
  const [dashboard, setDashboard] = useState<any>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [theme, setTheme] = useState<string | null>(null)

  async function loadDashboard() {
    setLoading(true)
    setError('')
    try {
      setDashboard(await call('dashboard.get'))
    } catch (e: any) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadDashboard()
    call('settings.snapshot').then((s: any) => setTheme(s?.theme || 'System')).catch(() => {})
  }, [])

  const navigate = (key: string) => setPage(key)

  const activeItem = NAV_GROUPS.flatMap((g) => g.items).find((i) => i.key === page)

  return (
    <div className="grid h-screen w-screen grid-cols-[260px_1fr] overflow-hidden bg-background text-foreground">
      {/* Sleek Modern Left Sidebar */}
      <aside className="flex flex-col border-r border-sidebar-border bg-sidebar text-sidebar-foreground shadow-xs">
        {/* Brand Header */}
        <div className="flex items-center gap-3 border-b border-sidebar-border/80 px-4 py-4.5">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary text-primary-foreground shadow-sm">
            <Zap className="h-5 w-5 fill-current" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-1.5">
              <span className="text-base font-black tracking-tight text-sidebar-foreground">IELTop</span>
              <Badge variant="secondary" className="h-4 rounded px-1.5 text-[9px] font-bold uppercase tracking-wider text-primary">
                Offline
              </Badge>
            </div>
            <p className="truncate text-xs font-medium text-sidebar-foreground/60">IELTS Practice Suite</p>
          </div>
        </div>

        {/* Navigation Categories */}
        <ScrollArea className="flex-1 px-3 py-4">
          <nav className="flex flex-col gap-6" aria-label="Main Navigation">
            {NAV_GROUPS.map((group) => (
              <div key={group.title} className="flex flex-col gap-1.5">
                <span className="px-3 text-[11px] font-bold tracking-wider text-sidebar-foreground/50">
                  {group.title}
                </span>
                {group.items.map((item) => {
                  const active = page === item.key
                  const Icon = item.Icon
                  return (
                    <button
                      key={item.key}
                      type="button"
                      aria-current={active ? 'page' : undefined}
                      onClick={() => setPage(item.key)}
                      className={cn(
                        'group flex items-center justify-between rounded-lg px-3 py-2.5 text-left text-sm font-medium transition-all duration-150',
                        active
                          ? 'border-l-[3px] border-primary bg-primary/10 font-semibold text-primary dark:bg-primary/20 dark:text-primary-foreground'
                          : 'text-sidebar-foreground/75 hover:bg-sidebar-accent hover:text-sidebar-foreground'
                      )}
                    >
                      <div className="flex items-center gap-3 min-w-0">
                        <Icon className={cn(
                          'h-4 w-4 shrink-0 transition-transform group-hover:scale-110',
                          active ? 'text-primary dark:text-primary-foreground' : 'text-sidebar-foreground/60 group-hover:text-sidebar-foreground'
                        )} />
                        <span className="truncate">{item.label}</span>
                      </div>
                      {item.tag && (
                        <span className={cn(
                          'rounded-md px-1.5 py-0.5 text-[10px] font-bold tracking-tight',
                          active
                            ? 'bg-primary text-primary-foreground'
                            : 'bg-sidebar-accent text-sidebar-foreground/70'
                        )}>
                          {item.tag}
                        </span>
                      )}
                    </button>
                  )
                })}
              </div>
            ))}
          </nav>
        </ScrollArea>

        {/* Quick Exam CTA Card inside Sidebar */}
        <div className="px-3 pb-3">
          <div className="rounded-xl border border-sidebar-border bg-sidebar-accent/50 p-3 shadow-2xs">
            <div className="flex items-center gap-2 text-xs font-semibold text-sidebar-foreground">
              <Clock className="h-3.5 w-3.5 text-primary" />
              <span>Timed Mock Exam</span>
            </div>
            <p className="mt-1 text-[11px] leading-relaxed text-sidebar-foreground/65">
              Simulate test day conditions with strict timing and native audio.
            </p>
            <Button
              size="sm"
              className="mt-2.5 h-7 w-full gap-1.5 text-xs font-semibold"
              onClick={() => setPage('mock')}
            >
              <Play className="h-3 w-3 fill-current" />
              <span>Launch Test</span>
            </Button>
          </div>
        </div>

        {/* Sidebar Footer with Native Status and Theme */}
        <div className="flex flex-col gap-2.5 border-t border-sidebar-border/80 p-3">
          <div className="flex items-center justify-between px-1 text-xs text-sidebar-foreground/60">
            <div className="flex items-center gap-1.5">
              <span className="relative flex h-2 w-2">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
                <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
              </span>
              <span className="font-medium">Core Engine Ready</span>
            </div>
            <span className="font-mono text-[11px] text-sidebar-foreground/50">v{dashboard?.version ?? '1.0.0'}</span>
          </div>

          <ThemeToggle
            value={theme}
            onChanged={(t) => {
              setTheme(t)
              call('settings.setTheme', { value: t }).catch(() => {})
            }}
          />
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="flex min-w-0 flex-col overflow-hidden bg-background">
        {/* Top Header Bar */}
        <header className="flex h-14 shrink-0 items-center justify-between border-b border-border bg-card/60 px-8 backdrop-blur-xs">
          <div className="flex items-center gap-3 min-w-0">
            <h1 className="text-base font-bold tracking-tight text-foreground truncate">
              {activeItem?.label}
            </h1>
            <span className="hidden text-xs text-muted-foreground sm:inline">
              {activeItem?.desc}
            </span>
          </div>

          <div className="flex items-center gap-3">
            {dashboard?.streakDays > 0 && (
              <Badge variant="outline" className="gap-1 border-amber-500/30 bg-amber-500/10 px-2.5 py-1 text-xs font-semibold text-amber-700 dark:text-amber-300">
                <Flame className="h-3.5 w-3.5 fill-current" />
                <span>{dashboard.streakDays} day streak</span>
              </Badge>
            )}

            {page !== 'mock' && (
              <Button size="sm" onClick={() => setPage('mock')} className="gap-1.5 text-xs font-semibold shadow-2xs">
                <Play className="h-3.5 w-3.5 fill-current" />
                <span>Start Mock Test</span>
              </Button>
            )}
          </div>
        </header>

        {/* Content View Container */}
        <main className="min-w-0 flex-1 overflow-hidden">
          <ScrollArea className="h-full">
            <div className="mx-auto w-full max-w-6xl px-8 py-8">
              {page === 'overview' && (
                <Overview
                  data={dashboard}
                  loading={loading}
                  error={error}
                  onRefresh={loadDashboard}
                  onNavigate={navigate}
                />
              )}
              {page === 'mock' && <MockTest />}
              {page === 'library' && <Library onNavigate={navigate} />}
              {page === 'editor' && <Editor onNavigate={navigate} />}
              {page === 'results' && <Results />}
              {page === 'servers' && <Servers onNavigate={navigate} />}
              {page === 'settings' && <Settings />}
            </div>
          </ScrollArea>
        </main>
      </div>
    </div>
  )
}
