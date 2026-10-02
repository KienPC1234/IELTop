import { useState } from 'react'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

/// A small confirm dialog used before a destructive action. The host never
/// shows a native prompt, so every delete asks here first.
export function Confirm({
  open,
  title,
  body,
  confirmLabel = 'Delete',
  onConfirm,
  onCancel,
}: {
  open: boolean
  title: string
  body?: React.ReactNode
  confirmLabel?: string
  onConfirm?: () => void
  onCancel?: () => void
}) {
  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) onCancel?.() }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {body && <DialogDescription>{body}</DialogDescription>}
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={onCancel}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/// One page title block, used by every page so the heading, spacing, and
/// description look the same. Keep titles short and descriptions to one line.
export function PageHeader({
  title,
  description,
  action,
}: {
  title: React.ReactNode
  description?: React.ReactNode
  action?: React.ReactNode
}) {
  return (
    <header className="mb-6 flex flex-wrap items-center justify-between gap-4 border-b border-border/40 pb-5">
      <div className="min-w-0">
        <h2 className="text-xl font-bold tracking-tight text-foreground">{title}</h2>
        {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
      </div>
      {action && <div className="flex shrink-0 flex-wrap items-center gap-2.5">{action}</div>}
    </header>
  )
}

/// A calm empty box for lists with no rows yet. Explains what to do next
/// instead of leaving a blank card.
export function EmptyState({
  title = 'Nothing here yet',
  body,
  action,
}: {
  title?: React.ReactNode
  body?: React.ReactNode
  action?: React.ReactNode
}) {
  return (
    <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-muted/20 px-6 py-10 text-center">
      <p className="text-sm font-semibold text-foreground">{title}</p>
      {body && <p className="mt-1 max-w-md text-xs leading-relaxed text-muted-foreground">{body}</p>}
      {action && <div className="mt-4 flex flex-wrap justify-center gap-2">{action}</div>}
    </div>
  )
}
/// A dismissible error bar for when a page already has content but an action
/// failed. Keeps the page usable instead of replacing it with a bare error.
export function ErrorBar({
  message,
  onDismiss,
}: {
  message?: string | null
  onDismiss?: () => void
}) {
  if (!message) return null
  return (
    <div className="mb-4 flex items-center justify-between gap-3 rounded-lg border border-destructive/30 bg-destructive/10 px-3.5 py-2.5 text-sm leading-relaxed text-destructive" role="alert">
      <span className="min-w-0">{message}</span>
      <Button variant="ghost" size="icon-sm" onClick={onDismiss} aria-label="Dismiss">
        <X className="h-4 w-4" />
      </Button>
    </div>
  )
}

/// Shared file import widget: a button that opens the picker and a drop zone.
export function FileImport({
  label,
  accept,
  onFiles,
  disabled,
  hint = 'or drop files here',
}: {
  label: React.ReactNode
  accept?: string
  onFiles: (files: File[]) => void
  disabled?: boolean
  hint?: string
}) {
  const [drag, setDrag] = useState(false)
  return (
    <div
      className={`mb-3 flex flex-wrap items-center gap-3 rounded-lg border border-dashed px-3.5 py-3 transition-colors ${
        drag ? 'border-primary bg-primary/5' : 'border-border bg-muted/30'
      } ${disabled ? 'opacity-60' : ''}`}
      onDragOver={(e) => {
        e.preventDefault()
        if (!disabled) setDrag(true)
      }}
      onDragLeave={() => setDrag(false)}
      onDrop={(e) => {
        e.preventDefault()
        setDrag(false)
        if (disabled) return
        const files = [...(e.dataTransfer?.files ?? [])]
        if (files.length) onFiles(files)
      }}
    >
      <Button asChild variant="outline" size="sm" disabled={disabled}>
        <label className="cursor-pointer">
          {label}
          <input
            type="file"
            accept={accept}
            multiple
            disabled={disabled}
            className="hidden"
            onChange={(e) => {
              const files = [...(e.target.files ?? [])]
              if (files.length) onFiles(files)
              e.target.value = ''
            }}
          />
        </label>
      </Button>
      <span className="text-sm text-muted-foreground">{hint}</span>
    </div>
  )
}
