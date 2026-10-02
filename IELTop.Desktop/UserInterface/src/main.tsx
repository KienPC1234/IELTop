import React from 'react'
import { createRoot } from 'react-dom/client'
import { TooltipProvider } from '@/components/ui/tooltip'
import { Toaster } from '@/components/ui/sonner'
import { applyStoredTheme } from './lib/theme.js'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import App from './App'
import './styles.css'

// Apply the saved theme before the first paint so there is no flash.
applyStoredTheme()

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <ErrorBoundary>
      <TooltipProvider delayDuration={300}>
        <App />
        <Toaster position="bottom-right" richColors />
      </TooltipProvider>
    </ErrorBoundary>
  </React.StrictMode>
)
