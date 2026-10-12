import { useEffect, useState } from 'react'
import {
  BarChart3,
  BookOpen,
  Clock,
  FileText,
  Flame,
  GraduationCap,
  LayoutDashboard,
  PanelLeftClose,
  PanelLeftOpen,
  Play,
  Server,
  Settings as SettingsIcon,
} from 'lucide-react'
import { call } from '@/bridge'
import { Button } from '@/components/ui/button'
import GuideDialog from '@/components/GuideDialog'
import { ScrollArea } from '@/components/ui/scroll-area'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import Overview from '@/pages/Overview'
import MockTest from '@/pages/MockTest'
import Library from '@/pages/Library'
import Study from '@/pages/Study'
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
    title: 'LEARNING',
    items: [
      { key: 'study', label: 'Study', Icon: GraduationCap, desc: 'Tutor chat, practice sets, vocabulary' },
    ],
  },
  {
    title: 'SYSTEM',
    items: [
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
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem('ieltop_sidebar_collapsed') === 'true'
    } catch {
      return false
    }
  })

  function toggleCollapsed() {
    setCollapsed((prev) => {
      const next = !prev
      try {
        localStorage.setItem('ieltop_sidebar_collapsed', String(next))
      } catch {}
      return next
    })
  }

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
    <div
      className={cn(
        'grid h-screen w-screen overflow-hidden bg-background text-foreground transition-all duration-200',
        collapsed ? 'grid-cols-[68px_1fr]' : 'grid-cols-[240px_1fr]'
      )}
    >
      {/* Sleek Modern Left Sidebar */}
      <aside className="flex flex-col border-r border-sidebar-border bg-sidebar text-sidebar-foreground shadow-xs select-none">
        {/* Brand Header with Collapse Toggle */}
        <div
          className={cn(
            'flex items-center border-b border-sidebar-border/80 py-3.5',
            collapsed ? 'flex-col gap-2.5 px-2' : 'justify-between px-3.5'
          )}
        >
          <div className="flex items-center gap-2.5 min-w-0">
            <img
              src="./assets/IELTop-red-symbol.svg"
              alt="IELTop logo"
              className="h-7 w-7 shrink-0 rounded-lg shadow-sm"
            />
            {!collapsed && (
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <span className="text-sm font-bold tracking-tight text-sidebar-foreground leading-none">IELTop</span>
                  <span className="inline-flex items-center justify-center rounded bg-muted/80 px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-widest text-primary leading-none border border-border/40">
                    Offline
                  </span>
                </div>
              </div>
            )}
          </div>
          <Button
            size="icon"
            variant="ghost"
            className="h-7 w-7 text-sidebar-foreground/60 hover:text-sidebar-foreground shrink-0"
            onClick={toggleCollapsed}
            title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            {collapsed ? <PanelLeftOpen className="h-4 w-4" /> : <PanelLeftClose className="h-4 w-4" />}
          </Button>
        </div>

        {/* Navigation Categories */}
        <ScrollArea className="flex-1 px-2 py-3">
          <nav className="flex flex-col gap-4" aria-label="Main Navigation">
            {NAV_GROUPS.map((group) => (
              <div key={group.title} className="flex flex-col gap-1">
                {!collapsed && (
                  <span className="px-2.5 text-[10px] font-bold tracking-wider text-sidebar-foreground/50 uppercase">
                    {group.title}
                  </span>
                )}
                {group.items.map((item) => {
                  const active = page === item.key
                  const Icon = item.Icon
                  return (
                    <button
                      key={item.key}
                      type="button"
                      aria-current={active ? 'page' : undefined}
                      onClick={() => setPage(item.key)}
                      title={collapsed ? item.label : undefined}
                      className={cn(
                        'group flex items-center rounded-lg text-sm font-medium transition-all duration-150',
                        collapsed
                          ? 'justify-center px-0 py-2.5 h-10 w-10 mx-auto'
                          : 'justify-between px-2.5 py-2 text-left w-full',
                        active
                          ? collapsed
                            ? 'bg-primary/15 text-primary dark:bg-primary/25 dark:text-primary-foreground font-semibold ring-1 ring-primary/40'
                            : 'border-l-[3px] border-primary bg-primary/10 font-semibold text-primary dark:bg-primary/20 dark:text-primary-foreground'
                          : 'text-sidebar-foreground/75 hover:bg-sidebar-accent hover:text-sidebar-foreground'
                      )}
                    >
                      <div className={cn('flex items-center min-w-0', collapsed ? 'justify-center' : 'gap-2.5')}>
                        <Icon
                          className={cn(
                            'h-4 w-4 shrink-0 transition-transform group-hover:scale-110',
                            active
                              ? 'text-primary dark:text-primary-foreground'
                              : 'text-sidebar-foreground/60 group-hover:text-sidebar-foreground'
                          )}
                        />
                        {!collapsed && <span className="truncate">{item.label}</span>}
                      </div>
                      {!collapsed && item.tag && (
                        <span
                          className={cn(
                            'rounded-md px-1.5 py-0.5 text-[10px] font-bold tracking-tight',
                            active ? 'bg-primary text-primary-foreground' : 'bg-sidebar-accent text-sidebar-foreground/70'
                          )}
                        >
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

        {/* Quick Exam CTA Card inside Sidebar (when expanded) */}
        {!collapsed && (
          <div className="px-2.5 pb-2.5">
            <div className="rounded-xl border border-sidebar-border bg-sidebar-accent/50 p-2.5 shadow-2xs">
              <div className="flex items-center gap-1.5 text-xs font-semibold text-sidebar-foreground">
                <Clock className="h-3.5 w-3.5 text-primary" />
                <span>Timed Mock Exam</span>
              </div>
              <p className="mt-1 text-[11px] leading-relaxed text-sidebar-foreground/65">
                Simulate test day conditions with strict timing.
              </p>
              <Button
                size="sm"
                className="mt-2 h-6.5 w-full gap-1.5 text-xs font-semibold"
                onClick={() => setPage('mock')}
              >
                <Play className="h-3 w-3 fill-current" />
                <span>Launch Test</span>
              </Button>
            </div>
          </div>
        )}

        {/* Sidebar Footer with Native Status and Theme */}
        <div className="flex flex-col gap-2 border-t border-sidebar-border/80 p-2.5">
          {!collapsed && (
            <div className="flex items-center justify-between px-1 text-[11px] text-sidebar-foreground/60">
              <div className="flex items-center gap-1.5">
                <span className="inline-flex h-2 w-2 rounded-full bg-success" />
                <span className="font-medium">Core Engine Ready</span>
              </div>
              <span className="font-mono text-[10px] text-sidebar-foreground/50">v{dashboard?.version ?? '1.0.0'}</span>
            </div>
          )}

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
        <header className="flex h-13 shrink-0 items-center justify-between border-b border-border bg-card/60 px-6 backdrop-blur-xs">
          <div className="flex items-center gap-3 min-w-0">
            <h1 className="text-base font-bold tracking-tight text-foreground truncate">
              {activeItem?.label}
            </h1>
            <span className="hidden text-xs text-muted-foreground md:inline">
              {activeItem?.desc}
            </span>
          </div>

          <div className="flex items-center gap-2">
            <GuideDialog />
            {dashboard?.streakDays > 0 && (
              <Badge variant="outline" className="gap-1 border-warning/30 bg-warning/10 px-2.5 py-1 text-xs font-semibold text-warning">
                <Flame className="h-3.5 w-3.5 fill-current" />
                <span>{dashboard.streakDays} day streak</span>
              </Badge>
            )}

            {page !== 'mock' && (
              <Button size="sm" onClick={() => setPage('mock')} className="gap-1.5 text-xs font-semibold shadow-2xs h-8">
                <Play className="h-3.5 w-3.5 fill-current" />
                <span>Start Mock Test</span>
              </Button>
            )}
          </div>
        </header>

        {/* Content View Container */}
        <main className="min-w-0 flex-1 overflow-hidden">
          <ScrollArea className="h-full">
            <div
              className={cn(
                'mx-auto w-full transition-all',
                page === 'study' ? 'max-w-[1440px] px-4 py-4 lg:px-6' : 'max-w-6xl px-6 py-6'
              )}
            >
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
              {page === 'study' && <Study onNavigate={navigate} />}
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
