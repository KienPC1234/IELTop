// Bridge to the .NET host. Supports Microsoft Edge WebView2
// (window.chrome.webview) in .NET MAUI and fallback hosts (window.external).
// Every call is a JSON request with an id and resolves to the matching reply,
// so the UI code reads like plain async calls. Pushed events (exam state,
// record commands) arrive on the same channel with an `event` field.

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: any) => void
        addEventListener: (event: string, handler: (event: any) => void) => void
      }
    }
  }
  interface External {
    sendMessage?: (message: any) => void
    receiveMessage?: (handler: (data: any) => void) => void
    [key: string]: any
  }
}

function hasHost() {
  return typeof window !== 'undefined' && (!!window.chrome?.webview?.postMessage || !!window.external?.sendMessage)
}

export const hasDesktopHost = typeof window !== 'undefined' && (!!window.chrome?.webview?.postMessage || !!window.external?.sendMessage)

// A per call timeout. Long model work is allowed more time than a quick page
// read, so the caller can pass its own budget.
const DEFAULT_TIMEOUT_MS = 120000
const LONG_TIMEOUT_MS = 300000

let nextId = 0
const pending = new Map<string, { resolve: (val: any) => void; reject: (err: any) => void; timer: any; method: string }>()
const listeners = new Set<(event: any) => void>()

/// One bridge failure with the short English line the host sent. A page can
/// show `error.message` directly; it is never a stack trace.
export class BridgeError extends Error {
  method?: string
  constructor(message: string, method?: string) {
    super(message)
    this.name = 'BridgeError'
    this.method = method
  }
}

function handleIncomingMessage(raw) {
  let reply
  try {
    reply = typeof raw === 'string' ? JSON.parse(raw) : raw
  } catch {
    return
  }

  // A pushed event, not a reply to a call.
  if (reply.event) {
    for (const fn of listeners) {
      try {
        fn(reply)
      } catch (e) {
        console.error('event listener failed', e)
      }
    }
    return
  }

  const entry = pending.get(reply.id)
  if (!entry) return
  pending.delete(reply.id)
  clearTimeout(entry.timer)
  if (reply.error) entry.reject(new BridgeError(reply.error, entry.method))
  else entry.resolve(reply.result)
}

let webviewBound = false
let externalBound = false

function ensureListenersAttached() {
  if (typeof window === 'undefined') return
  if (window.chrome?.webview?.addEventListener && !webviewBound) {
    webviewBound = true
    window.chrome.webview.addEventListener('message', (event) => handleIncomingMessage(event.data))
  }
  if (window.external?.receiveMessage && !externalBound) {
    externalBound = true
    window.external.receiveMessage(handleIncomingMessage)
  }
}

if (typeof window !== 'undefined') {
  ensureListenersAttached()
  window.addEventListener('DOMContentLoaded', ensureListenersAttached)
}

function postToHost(jsonString) {
  ensureListenersAttached()
  if (window.chrome?.webview?.postMessage) {
    window.chrome.webview.postMessage(jsonString)
  } else if (window.external?.sendMessage) {
    window.external.sendMessage(jsonString)
  } else {
    throw new Error('No host communication channel available')
  }
}

// Only the desktop build has the host. Outside it, calls fall back so the UI
// can still be worked on in a browser, never a crash.
const sampleDashboard = {
  version: '1.0.0',
  examAttempts: 0,
  lastBandLabel: 'No test yet',
  llmConfigured: false,
  llmSummary: 'Not set',
  modelsReady: 0,
  modelsTotal: 4,
  streakDays: 0,
  activeToday: false,
  streakLabel: 'No streak yet',
  streakHint: 'Finish a test or a speaking practice to start a streak.',
  weeklyActivity: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map((label) => ({
    label,
    count: 0,
  })),
  bandTrend: [],
}

/// Calls one host method and resolves with its result. Rejects with a
/// BridgeError carrying the host's short English message on failure.
export async function call<T = any>(method: string, args: any = {}, options: { timeoutMs?: number; long?: boolean } = {}): Promise<T> {
  ensureListenersAttached()
  if (!hasHost()) {
    // Allow WebView2 injection a brief 100ms grace period on initial load
    for (let i = 0; i < 5 && !hasHost(); i++) {
      await new Promise((r) => setTimeout(r, 20))
      ensureListenersAttached()
    }
  }

  if (!hasHost()) {
    if (method === 'dashboard.get') return Promise.resolve(sampleDashboard as unknown as T)
    return Promise.reject(new BridgeError('This action needs the desktop app.', method))
  }

  const timeout = options.timeoutMs ?? (options.long ? LONG_TIMEOUT_MS : DEFAULT_TIMEOUT_MS)
  const id = String(++nextId)
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      if (pending.delete(id)) reject(new BridgeError('The action took too long. Try again.', method))
    }, timeout)
    pending.set(id, { resolve, reject, timer, method })
    try {
      postToHost(JSON.stringify({ id, method, args }))
    } catch (e) {
      pending.delete(id)
      clearTimeout(timer)
      reject(new BridgeError('The app could not reach its host. Restart the app.', method))
    }
  })
}

/// Subscribes to pushed events. Returns an unsubscribe function.
export function onEvent(fn: (event: any) => void): () => void {
  listeners.add(fn)
  return () => {
    listeners.delete(fn)
  }
}

/// Closes the exam window. The host close runs first so its state is cleared;
/// closing from inside the window always runs on the right thread, so it is
/// also attempted as a fallback when the host close cannot run.
export async function closeExamWindow() {
  try {
    await call('exam.closeWindow')
  } catch {
    /* fall through to the in-window close below */
  }
  try {
    window.close()
  } catch {
    /* the host close already handled it */
  }
}
