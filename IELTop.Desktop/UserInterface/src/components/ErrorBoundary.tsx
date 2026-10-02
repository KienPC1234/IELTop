import React from 'react'
import { AlertCircle, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface ErrorBoundaryProps {
  children?: React.ReactNode
  fallbackAction?: React.ReactNode
}

interface ErrorBoundaryState {
  hasError: boolean
  error: Error | null
}

export class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    console.error('Unhandled UI exception:', error, errorInfo)
  }

  handleReload = () => {
    window.location.reload()
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="grid h-screen place-items-center bg-background p-6">
          <div className="w-[520px] max-w-full rounded-lg border border-destructive/30 bg-destructive/5 p-6 shadow-sm">
            <div className="flex items-center gap-3 text-destructive">
              <AlertCircle className="h-5 w-5 shrink-0" />
              <h2 className="text-base font-semibold">An unexpected interface error occurred</h2>
            </div>
            <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
              {this.state.error?.message || 'The application encountered an error while rendering this page.'}
            </p>
            <div className="mt-5 flex gap-2">
              <Button onClick={this.handleReload} size="sm">
                <RefreshCw className="mr-2 h-4 w-4" /> Reload view
              </Button>
              {this.props.fallbackAction}
            </div>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}

