import { Monitor, Moon, Sun } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useTheme, setThemeChoice } from '@/lib/theme'

const OPTIONS = [
  { value: 'System', label: 'System', Icon: Monitor },
  { value: 'Light', label: 'Light', Icon: Sun },
  { value: 'Dark', label: 'Dark', Icon: Moon },
]

/// A small menu that switches the colour theme. Kept in one place so the main
/// window and the exam window show the same control.
export function ThemeToggle({ value, onChanged }) {
  const [theme, setTheme] = useTheme(value || 'System')
  const current = OPTIONS.find((o) => o.value === theme) || OPTIONS[0]
  const Icon = current.Icon

  function choose(next) {
    const applied = setThemeChoice(next)
    setTheme(applied)
    onChanged?.(applied)
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          className="w-full justify-start gap-2 border-sidebar-border bg-transparent text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-foreground"
        >
          <Icon className="h-4 w-4" />
          <span>{current.label}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {OPTIONS.map((o) => (
          <DropdownMenuItem key={o.value} onSelect={() => choose(o.value)}>
            <o.Icon className="mr-2 h-4 w-4" />
            {o.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
