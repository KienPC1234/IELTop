import { useCallback, useEffect, useState } from 'react'
import { call } from './bridge.js'

/// Loads one page snapshot and gives the page a single runner that both acts
/// and applies the fresh snapshot the host returns. Every page uses this, so
/// the loading, error, and busy handling stays the same everywhere.
export function usePage(snapshotMethod) {
  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)

  const reload = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setData(await call(snapshotMethod))
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }, [snapshotMethod])

  useEffect(() => {
    reload()
  }, [reload])

  const run = useCallback(async (method, args) => {
    setBusy(true)
    setError('')
    try {
      const next = await call(method, args)
      if (next !== undefined && next !== null) setData(next)
      return next
    } catch (e) {
      setError(e.message)
      return null
    } finally {
      setBusy(false)
    }
  }, [])

  return { data, setData, error, setError, loading, busy, reload, run }
}

/// Reads a picked or dropped file as text, with a short size guard.
export function readTextFile(file, maxBytes = 8 * 1024 * 1024) {
  return new Promise((resolve, reject) => {
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
export async function readImageFiles(files, maxBytes = 6 * 1024 * 1024) {
  const out = []
  for (const file of files) {
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
export function isBinaryDocument(fileName) {
  const lower = (fileName || '').toLowerCase()
  return BINARY_DOCS.some((ext) => lower.endsWith(ext))
}

/// Reads any file as base64, for the host to decode (pdf, docx).
export function readFileBase64(file, maxBytes = 12 * 1024 * 1024) {
  return new Promise((resolve, reject) => {
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
