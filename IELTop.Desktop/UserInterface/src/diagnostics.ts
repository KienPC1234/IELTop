import { call } from '@/bridge'

// The page side of the diagnostics system. Uncaught JavaScript errors and
// unhandled promise rejections are sent to the host, which writes them into the
// one app log next to the C# errors. Without this a page fault only shows in a
// console nobody can open in the packaged app.
//
// Reporting is fire and forget and never throws: the error reporter must not be
// the thing that breaks the page.

let installed = false
let errorCount = 0

function send(method: string, args: Record<string, unknown>) {
  try {
    // The call itself may fail while the host is starting; that is fine.
    void call(method, args).catch(() => {})
  } catch {
    /* the reporter must never throw */
  }
}

export function installErrorReporting() {
  if (installed || typeof window === 'undefined') return
  installed = true

  window.addEventListener('error', (event) => {
    errorCount++
    const error = event.error as Error | undefined
    send('diagnostics.clientError', {
      message: String(event.message ?? error?.message ?? 'unknown error'),
      source: String(event.filename ?? ''),
      line: String(event.lineno ?? ''),
      stack: String(error?.stack ?? '').slice(0, 4000),
    })
  })

  window.addEventListener('unhandledrejection', (event) => {
    errorCount++
    const reason = event.reason as any
    send('diagnostics.clientError', {
      message: 'Unhandled promise rejection: ' + String(reason?.message ?? reason ?? 'unknown'),
      source: 'promise',
      line: '',
      stack: String(reason?.stack ?? '').slice(0, 4000),
    })
  })
}

/// A deliberate note from the page, so "what I just did" stands out in the log.
export function logNote(message: string) {
  send('diagnostics.clientLog', { level: 'note', message })
}

/// How many page errors were seen, shown on the Diagnostics tab.
export function pageErrorCount() {
  return errorCount
}
