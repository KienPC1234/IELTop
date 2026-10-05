import React from 'react'
import { createRoot } from 'react-dom/client'
import { TooltipProvider } from '@/components/ui/tooltip'
import { Toaster } from '@/components/ui/sonner'
import { applyStoredTheme, applyExamTheme } from './lib/theme.js'
import { installErrorReporting } from './diagnostics'
import ExamWindow from './ExamWindow'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { Button } from '@/components/ui/button'
import { call, closeExamWindow } from '@/bridge'
import './styles.css'

// The exam runs in its own native window. It always opens light, like the
// real test; the student can switch it dark from the exam top bar.
applyStoredTheme()
applyExamTheme()

// Page faults in the exam window go to the host log, like the main window.
installErrorReporting()

// No right click menu during a test, so the page content cannot be inspected
// from the exam window.
window.addEventListener('contextmenu', (e) => e.preventDefault())

// The page crashed, so the React tree that normally asks before discarding a
// running test is gone. The browser question is the fallback so this button
// still does something the student can see.
async function closeFromCrashScreen() {
  const answer = await closeExamWindow()
  if (answer?.needsConfirm) {
    const keep = window.confirm('This test has not been submitted. Closing the window discards the answers.')
    if (keep) await call('exam.confirmCloseWindow')
  }
}

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ErrorBoundary
      fallbackAction={(
        <Button variant="outline" size="sm" onClick={closeFromCrashScreen}>
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
