import {
  CircleCheckIcon,
  InfoIcon,
  Loader2Icon,
  OctagonXIcon,
  TriangleAlertIcon,
} from 'lucide-react'
import { Toaster as Sonner } from 'sonner'

// The app owns the theme (see lib/theme.js), so the toaster reads it from the
// document class instead of pulling a second theme provider.
function currentTheme() {
  if (typeof document === 'undefined') return 'system'
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light'
}

const Toaster = ({ ...props }: React.ComponentProps<typeof Sonner>) => (
  <Sonner
    theme={currentTheme()}
    className="toaster group"
    icons={{
      success: <CircleCheckIcon className="size-4" />,
      info: <InfoIcon className="size-4" />,
      warning: <TriangleAlertIcon className="size-4" />,
      error: <OctagonXIcon className="size-4" />,
      loading: <Loader2Icon className="size-4 animate-spin" />,
    }}
    style={{
      '--normal-bg': 'var(--popover)',
      '--normal-text': 'var(--popover-foreground)',
      '--normal-border': 'var(--border)',
      '--border-radius': 'var(--radius)',
    } as React.CSSProperties}
    {...props}
  />
)

export { Toaster }
