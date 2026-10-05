// Bridge to the .NET host. The main window runs inside BlazorWebView, so the
// primary channel is a Blazor object the host hands down (JS interop). When the
// page is hosted by a plain WebView2 instead, the HTTP loopback (/api/call,
// /api/events) is the channel. Both run the same host handlers.
//
// Pushed events (exam state, audio trigger, clock tick) arrive over the Blazor
// channel or over Server-Sent Events, whichever is active.

declare global {
  interface Window {
    /// The object Blazor exports. Calls go through it when present.
    __ieltopsBridge?: {
      invokeMethodAsync: (method: string, id: string, name: string, argsJson: string) => Promise<string>
    }
    /// Called by the Blazor host component once, on its first render.
    ieltopsBridgeAttach?: (bridge: any) => void
    /// Called by the Blazor host component for every pushed event.
    ieltopsBridgePush?: (json: string) => void
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

interface CallOptions {
  timeoutMs?: number
  long?: boolean
  signal?: AbortSignal
}

// The HTTP channel belongs to StaticFileServer, which only ever listens on
// http://127.0.0.1. The Blazor virtual host is https://0.0.0.1. Testing the
// scheme, not just the "http" prefix, keeps the two apart: a prefix test also
// matches the virtual host and sends its requests to a host that is not there.
const isLoopbackHost = typeof window !== 'undefined' && window.location.protocol === 'http:'

// The WebView2 message channel belongs to the exam window, which the host wires
// to DesktopBrowser. On the Blazor window the same channel exists but carries
// Blazor's own interop traffic, so posting a bridge call there reaches nobody and
// would leave the caller waiting for an answer that never comes.
function hasNativeHost() {
  return (
    isLoopbackHost &&
    typeof window !== 'undefined' &&
    (!!window.chrome?.webview?.postMessage || !!window.external?.sendMessage)
  )
}

function hasBlazorBridge() {
  return typeof window !== 'undefined' && !!window.__ieltopsBridge
}

const DEFAULT_TIMEOUT_MS = 120000
const LONG_TIMEOUT_MS = 300000

let nextId = 0
const pending = new Map<string, { resolve: (val: any) => void; reject: (err: any) => void; timer: any; method: string }>()
const listeners = new Set<(event: any) => void>()

/// One bridge failure with the short English line the host sent.
class BridgeError extends Error {
  method?: string
  constructor(message: string, method?: string) {
    super(message)
    this.name = 'BridgeError'
    this.method = method
  }
}

// Resolves the first time Blazor hands the bridge down. A page that is not
// hosted by BlazorWebView never resolves this, and the HTTP channel is used.
let bridgeReadyResolve: (() => void) | null = null
const bridgeReady = new Promise<void>((resolve) => {
  bridgeReadyResolve = resolve
})

function attachBlazorBridge(bridge: any) {
  window.__ieltopsBridge = bridge
  bridgeReadyResolve?.()
  bridgeReadyResolve = null
}

function pushIncomingEvent(json: string) {
  dispatchIncomingEvent(safeParse(json))
}

if (typeof window !== 'undefined') {
  // The host calls these two names. Defining them here, before the Blazor
  // runtime boots, closes the gap between the page loading and the first call.
  window.ieltopsBridgeAttach = attachBlazorBridge
  window.ieltopsBridgePush = pushIncomingEvent
}

/// Waits briefly for Blazor to hand down the bridge. Returns straight away on a
/// host that has no Blazor runtime, so the HTTP channel is not delayed.
async function waitForBlazorBridge(ms = 3000): Promise<boolean> {
  if (hasBlazorBridge()) return true
  if (typeof window === 'undefined' || typeof (window as any).Blazor === 'undefined') return false
  await Promise.race([bridgeReady, new Promise((r) => setTimeout(r, ms))])
  return hasBlazorBridge()
}

function safeParse(raw: any): any {
  if (typeof raw !== 'string') return raw
  try {
    return JSON.parse(raw)
  } catch {
    return null
  }
}

function dispatchIncomingEvent(reply: any) {
  if (!reply) return

  // Unpack nested payload if sent via broadcast wrapper
  let eventPayload = reply
  if (reply.event === 'exam.push' && reply.payload) {
    if (typeof reply.payload === 'string') {
      eventPayload = safeParse(reply.payload) ?? reply.payload
    } else {
      eventPayload = reply.payload
    }
  }

  // Notify the onEvent listeners
  for (const fn of listeners) {
    try {
      fn(eventPayload)
    } catch (e) {
      console.error('Event listener failed', e)
    }
  }
}

function handleIncomingMessage(raw: any) {
  const reply = safeParse(raw)
  if (!reply) return

  // A pushed event
  if (reply.event || reply.exam) {
    dispatchIncomingEvent(reply)
    return
  }

  // An RPC reply to a pending postMessage call
  if (reply.id) {
    const entry = pending.get(reply.id)
    if (!entry) return
    pending.delete(reply.id)
    clearTimeout(entry.timer)
    if (reply.error) entry.reject(new BridgeError(reply.error, entry.method))
    else entry.resolve(reply.result)
  }
}

let webviewBound = false
let externalBound = false
let sseInitialized = false

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

  // The event stream is the fallback push channel. When Blazor is present its
  // own push path is used, so opening a stream as well would deliver every
  // event twice.
  if (isLoopbackHost && !hasBlazorBridge() && !sseInitialized && typeof EventSource !== 'undefined') {
    sseInitialized = true
    try {
      const source = new EventSource('/api/events')
      source.onmessage = (event) => {
        dispatchIncomingEvent(safeParse(event.data))
      }
      source.onerror = () => {
        // SSE will automatically retry in the background
      }
    } catch {
      // EventSource failed to create, fallback to postMessage
    }
  }
}

if (typeof window !== 'undefined') {
  ensureListenersAttached()
  window.addEventListener('DOMContentLoaded', ensureListenersAttached)
}

function postToHost(jsonString: string) {
  ensureListenersAttached()
  if (window.chrome?.webview?.postMessage) {
    window.chrome.webview.postMessage(jsonString)
  } else if (window.external?.sendMessage) {
    window.external.sendMessage(jsonString)
  } else {
    throw new Error('No host communication channel available')
  }
}

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

/// Tells the host to stop a request that is still running. Used on timeout, so
/// a long action does not keep burning the host after the page gave up.
async function cancelCall(targetId: string): Promise<boolean> {
  if (!targetId) return false

  if (hasBlazorBridge()) {
    try {
      const reply = safeParse(await window.__ieltopsBridge!.invokeMethodAsync('CallAsync', String(++nextId), 'bridge.cancel', JSON.stringify({ targetId })))
      return reply?.result?.cancelled === true
    } catch {
      // Fall through to the channels below.
    }
  }

  if (isLoopbackHost) {
    try {
      const res = await fetch('/api/call', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id: String(++nextId), method: 'bridge.cancel', args: { targetId } }),
      })
      if (res.ok) {
        const body = await res.json()
        return body?.result?.cancelled === true
      }
    } catch {
      // Fall through to the window channel below.
    }
  }

  try {
    postToHost(JSON.stringify({ id: String(++nextId), method: 'bridge.cancel', args: { targetId } }))
    return true
  } catch {
    return false
  }
}

/// Calls one host method and resolves with its result. The Blazor interop
/// channel is used when available, then the HTTP loopback, then WebView2
/// postMessage, so the same page code works under either host.
export async function call<T = any>(
  method: string,
  args: any = {},
  options: CallOptions = {}
): Promise<T> {
  ensureListenersAttached()

  const id = String(++nextId)
  const timeoutMs = options.timeoutMs ?? (options.long ? LONG_TIMEOUT_MS : DEFAULT_TIMEOUT_MS)

  if (options.signal?.aborted) {
    throw new BridgeError('Action cancelled.', method)
  }

  // 1. Blazor JS interop, used by the main window under BlazorWebView.
  if (await waitForBlazorBridge()) {
    let timer: any = 0
    const timedOut = new Promise<never>((_, reject) => {
      timer = setTimeout(() => {
        cancelCall(id).catch(() => {})
        reject(new BridgeError('The action took too long. Try again.', method))
      }, timeoutMs)
    })

    const onAbort = () => {
      cancelCall(id).catch(() => {})
      rejectWithAbort()
    }
    let rejectWithAbort = () => {}
    const aborted = new Promise<never>((_, reject) => {
      rejectWithAbort = () => reject(new BridgeError('Action cancelled.', method))
      options.signal?.addEventListener('abort', onAbort, { once: true })
    })

    try {
      const reply = await Promise.race([
        window.__ieltopsBridge!.invokeMethodAsync('CallAsync', id, method, JSON.stringify(args ?? {})),
        timedOut,
        aborted,
      ])
      const body = safeParse(reply)
      if (body?.error) throw new BridgeError(body.error, method)
      return body?.result as T
    } finally {
      clearTimeout(timer)
      options.signal?.removeEventListener('abort', onAbort)
    }
  }

  // 2. Low-latency HTTP loopback, used by the plain WebView2 host.
  if (isLoopbackHost) {
    const controller = new AbortController()
    const timer = setTimeout(() => {
      controller.abort()
      cancelCall(id).catch(() => {})
    }, timeoutMs)

    if (options.signal) {
      options.signal.addEventListener(
        'abort',
        () => {
          controller.abort()
          cancelCall(id).catch(() => {})
        },
        { once: true }
      )
    }

    try {
      const res = await fetch('/api/call', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id, method, args }),
        signal: controller.signal,
      })

      clearTimeout(timer)

      if (!res.ok) {
        throw new BridgeError(`Host returned status ${res.status}.`, method)
      }

      const body = await res.json()
      if (body.error) {
        throw new BridgeError(body.error, method)
      }
      return body.result as T
    } catch (err: any) {
      clearTimeout(timer)
      if (err instanceof BridgeError) throw err
      if (controller.signal.aborted) {
        throw new BridgeError('The action timed out or was cancelled.', method)
      }
      // If HTTP call failed with a network error, fall through to postMessage fallback below
    }
  }

  // 3. Fallback to WebView2 / external postMessage channel
  if (!hasNativeHost()) {
    // Brief grace period for WebView2 script injection
    for (let i = 0; i < 5 && !hasNativeHost(); i++) {
      await new Promise((r) => setTimeout(r, 20))
      ensureListenersAttached()
    }
  }

  if (!hasNativeHost()) {
    if (method === 'dashboard.get') return Promise.resolve(sampleDashboard as unknown as T)
    return Promise.reject(new BridgeError('This action needs the desktop app.', method))
  }

  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      if (pending.delete(id)) {
        cancelCall(id).catch(() => {})
        reject(new BridgeError('The action took too long. Try again.', method))
      }
    }, timeoutMs)

    if (options.signal) {
      options.signal.addEventListener(
        'abort',
        () => {
          if (pending.delete(id)) {
            clearTimeout(timer)
            cancelCall(id).catch(() => {})
            reject(new BridgeError('Action cancelled.', method))
          }
        },
        { once: true }
      )
    }

    pending.set(id, { resolve, reject, timer, method })
    try {
      postToHost(JSON.stringify({ id, method, args }))
    } catch {
      pending.delete(id)
      clearTimeout(timer)
      reject(new BridgeError('The app could not reach its host. Restart the app.', method))
    }
  })
}

/// Registers a handler for pushed events. Returns the function that removes it.
export function onEvent(handler: (event: any) => void): () => void {
  ensureListenersAttached()
  listeners.add(handler)
  return () => {
    listeners.delete(handler)
  }
}

/// Asks the host to close the exam window. Returns the host answer, which is
/// `{ needsConfirm: true }` while a test is running: the window stays open and
/// the caller has to show the question first. Closing always goes through the
/// host, never through `window.close()`, which would leave an empty window.
export async function closeExamWindow() {
  try {
    return await call('exam.closeWindow')
  } catch {
    return null
  }
}
