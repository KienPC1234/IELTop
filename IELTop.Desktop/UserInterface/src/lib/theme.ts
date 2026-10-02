import { useEffect, useState } from 'react'

// The colour theme: System (follow the OS), Light, or Dark. The choice is kept
// in the host settings so it survives a restart, and mirrored in localStorage
// so the first paint never flashes the wrong theme while the host answers.

const STORAGE_KEY = 'ieltop.theme'
const media = typeof window !== 'undefined' && window.matchMedia
  ? window.matchMedia('(prefers-color-scheme: dark)')
  : null

function stored() {
  try {
    const v = localStorage.getItem(STORAGE_KEY)
    return v === 'Light' || v === 'Dark' || v === 'System' ? v : 'System'
  } catch {
    return 'System'
  }
}

function prefersDark() {
  return media ? media.matches : false
}

function apply(choice) {
  const dark = choice === 'Dark' || (choice === 'System' && prefersDark())
  document.documentElement.classList.toggle('dark', dark)
}

/// Applies a theme choice to the document and remembers it locally.
export function setThemeChoice(choice) {
  const value = choice === 'Light' || choice === 'Dark' ? choice : 'System'
  try { localStorage.setItem(STORAGE_KEY, value) } catch { /* private mode */ }
  apply(value)
  return value
}

/// Reads the theme from the host once, applies it, and follows the OS while the
/// choice stays System. Returns the current choice and a setter.
export function useTheme(initial = stored()) {
  const [theme, setTheme] = useState(initial)

  useEffect(() => {
    setThemeChoice(theme)
  }, [theme])

  useEffect(() => {
    if (!media) return undefined
    const onChange = () => { if (theme === 'System') apply('System') }
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [theme])

  return [theme, setTheme] as const
}

/// Applies the stored theme before React mounts, so there is no flash.
export function applyStoredTheme() {
  apply(stored())
}

// The exam window always starts light, like the real test. The student can
// switch it dark during a test; that choice lives in its own key so it never
// touches the app theme, and each native window owns its own document.

const EXAM_STORAGE_KEY = 'ieltop.exam-theme'

/// True when the student picked a dark background for the exam window.
export function readExamDark() {
  try {
    return localStorage.getItem(EXAM_STORAGE_KEY) === 'dark'
  } catch {
    return false
  }
}

/// Applies the exam background to this document only and remembers it.
export function applyExamDark(dark) {
  try { localStorage.setItem(EXAM_STORAGE_KEY, dark ? 'dark' : 'light') } catch { /* private mode */ }
  document.documentElement.classList.toggle('dark', dark)
}

/// Applies the stored exam background before React mounts.
export function applyExamTheme() {
  applyExamDark(readExamDark())
}
