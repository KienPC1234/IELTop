// Bridge to the .NET host. Photino exposes window.external.sendMessage and
// window.external.receiveMessage. Every call is a JSON request with an id and
// resolves to the matching reply, so the UI code reads like plain async calls.
// The host can also push unsolicited events (exam state, record commands),
// which arrive on the same channel with an `event` field.

function hasHost() {
  return typeof window !== 'undefined' && !!window.external?.sendMessage
}

let nextId = 0
const pending = new Map()
const listeners = new Set()

if (hasHost()) {
  window.external.receiveMessage((message) => {
    let reply
    try {
      reply = JSON.parse(message)
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
    if (reply.error) entry.reject(new Error(reply.error))
    else entry.resolve(reply.result)
  })
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

export function call(method, args = {}) {
  if (!hasHost()) {
    if (method === 'dashboard.get') return Promise.resolve(sampleDashboard)
    return Promise.reject(new Error('This action needs the desktop app.'))
  }

  const id = String(++nextId)
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject })
    window.external.sendMessage(JSON.stringify({ id, method, args }))
    setTimeout(() => {
      if (pending.delete(id)) reject(new Error('The action timed out.'))
    }, 120000)
  })
}

export function onEvent(fn) {
  listeners.add(fn)
  return () => listeners.delete(fn)
}
