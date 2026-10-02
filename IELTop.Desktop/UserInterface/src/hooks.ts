import { useCallback, useEffect, useRef, useState } from 'react'
import { call } from '@/bridge'

/// Loads one page snapshot and gives the page a single runner that both acts
/// and applies the fresh snapshot the host returns. Every page uses this, so
/// the loading, error, and busy handling stays the same everywhere.
export function usePage<T = any>(snapshotMethod: string) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setData(await call<T>(snapshotMethod))
    } catch (e: any) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }, [snapshotMethod])

  useEffect(() => {
    reload()
  }, [reload])

  const run = useCallback(async (method: string, args: any = {}) => {
    setBusy(true)
    setError('')
    try {
      const next = await call(method, args)
      if (next !== undefined && next !== null) setData(next)
      return next
    } catch (e: any) {
      setError(e.message)
      return null
    } finally {
      setBusy(false)
    }
  }, [])

  return { data, setData, error, setError, loading, busy, reload, run }
}

/// A text box that commits to the host after the student pauses, not on every
/// keystroke. The exam essay, transcript, and notes boxes use this: committing
/// per keystroke would spam the bridge and flicker every disabled button.
/// The pending edit flushes when the field loses focus, so switching parts or
/// pressing Submit after typing can never drop the last words.
export function useDraftText(value, resetKey, commit, delay = 500) {
  const [draft, setDraft] = useState(value)
  const draftRef = useRef(value)
  const timer = useRef(null)
  const commitRef = useRef(commit)
  commitRef.current = commit

  function flushPending() {
    if (timer.current) {
      clearTimeout(timer.current)
      timer.current = null
      commitRef.current(draftRef.current)
    }
  }

  // A new part takes the engine text. A pending edit for the old part flushes
  // first, so it is saved instead of dropped. The blur flush below normally
  // runs first; this is the backstop.
  useEffect(() => {
    flushPending()
    draftRef.current = value
    setDraft(value)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resetKey])

  useEffect(() => () => clearTimeout(timer.current), [])

  function change(next) {
    draftRef.current = next
    setDraft(next)
    clearTimeout(timer.current)
    timer.current = setTimeout(() => {
      timer.current = null
      commitRef.current(next)
    }, delay)
  }

  return [draft, change, flushPending]
}

/// Reads a picked or dropped file as text, with a short size guard.
export function readTextFile(file: File, maxBytes = 8 * 1024 * 1024): Promise<{ name: string; content: string }> {
  return new Promise<{ name: string; content: string }>((resolve, reject) => {
    if (file.size > maxBytes) {
      reject(new Error('That file is too large to import.'))
      return
    }
    const reader = new FileReader()
    reader.onload = () => resolve({ name: file.name, content: String(reader.result ?? '') })
    reader.onerror = () => reject(new Error('Could not read that file.'))
    reader.readAsText(file)
  })
}

/// Reads image files as base64 for the vision model. Only real images are
/// sent; a PDF or Word file has to be converted outside the app first.
export async function readImageFiles(files: FileList | File[], maxBytes = 6 * 1024 * 1024): Promise<Array<{ name: string; mediaType: string; base64: string }>> {
  const out: Array<{ name: string; mediaType: string; base64: string }> = []
  for (const file of Array.from(files)) {
    if (!/^image\/(png|jpe?g)$/i.test(file.type)) continue
    if (file.size > maxBytes) continue
    const buffer = await file.arrayBuffer()
    let binary = ''
    const bytes = new Uint8Array(buffer)
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i])
    out.push({
      name: file.name,
      mediaType: file.type || 'image/png',
      base64: btoa(binary),
    })
  }
  return out
}

/// The document extensions the host can turn into text.
const BINARY_DOCS = ['.pdf', '.docx']

/// True when a file has to be sent as bytes, not text.
export function isBinaryDocument(fileName: string) {
  const lower = (fileName || '').toLowerCase()
  return BINARY_DOCS.some((ext) => lower.endsWith(ext))
}

/// Reads any file as base64, for the host to decode (pdf, docx).
export function readFileBase64(file: File, maxBytes = 12 * 1024 * 1024): Promise<{ name: string; base64: string }> {
  return new Promise<{ name: string; base64: string }>((resolve, reject) => {
    if (file.size > maxBytes) {
      reject(new Error('That file is too large to import.'))
      return
    }
    const reader = new FileReader()
    reader.onload = () => {
      const result = String(reader.result ?? '')
      const comma = result.indexOf(',')
      resolve({ name: file.name, base64: comma >= 0 ? result.slice(comma + 1) : result })
    }
    reader.onerror = () => reject(new Error('Could not read that file.'))
    reader.readAsDataURL(file)
  })
}
