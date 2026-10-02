import React from 'react'
import { createRoot } from 'react-dom/client'
import { TooltipProvider } from '@/components/ui/tooltip'
import { Toaster } from '@/components/ui/sonner'
import { applyStoredTheme, applyExamTheme } from './lib/theme.js'
import ExamWindow from './ExamWindow'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { Button } from '@/components/ui/button'
import { closeExamWindow } from '@/bridge'
import './styles.css'

// The exam runs in its own native window. It always opens light, like the
// real test; the student can switch it dark from the exam top bar.
applyStoredTheme()
applyExamTheme()

// No right click menu during a test, so the page content cannot be inspected
// from the exam window.
window.addEventListener('contextmenu', (e) => e.preventDefault())

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <ErrorBoundary
      fallbackAction={(
        <Button variant="outline" size="sm" onClick={() => closeExamWindow()}>
          Close window
        </Button>
      )}
    >
      <TooltipProvider delayDuration={300}>
        <ExamWindow />
        <Toaster position="bottom-right" richColors />
      </TooltipProvider>
    </ErrorBoundary>
  </React.StrictMode>
)
