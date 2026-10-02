import type * as React from 'react'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

interface FieldProps {
  label?: React.ReactNode
  hint?: React.ReactNode
  children?: React.ReactNode
  className?: string
  htmlFor?: string
}

/// A labelled field: a Label above the control, used across every page so the
/// spacing and text stay the same.
export function Field({ label, hint, children, className, htmlFor }: FieldProps) {
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      {label && <Label htmlFor={htmlFor} className="text-sm font-semibold leading-relaxed">{label}</Label>}
      {children}
      {hint && <p className="text-sm leading-relaxed text-muted-foreground">{hint}</p>}
    </div>
  )
}

/// A two column grid for fields, used by the forms.
export function Grid2({ children, className }: { children?: React.ReactNode; className?: string }) {
  return <div className={cn('grid grid-cols-1 gap-3.5 md:grid-cols-2', className)}>{children}</div>
}
