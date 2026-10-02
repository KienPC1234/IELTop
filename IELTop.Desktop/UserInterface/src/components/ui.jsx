import { useState } from 'react'
import { call } from '../bridge.js'

/// A small confirm dialog used before a destructive action. The host never
/// shows a native prompt, so every delete asks here first.
export function Confirm({ open, title, body, confirmLabel = 'Delete', onConfirm, onCancel }) {
  if (!open) return null
  return (
    <div className="modal-backdrop" onClick={onCancel}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h3>{title}</h3>
        <p>{body}</p>
        <div className="actions">
          <button className="btn" onClick={onCancel}>
            Cancel
          </button>
          <button className="btn primary" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}

/// Shared file import widget: a button that opens the picker and a drop zone.
export function FileImport({ label, accept, onFiles, disabled }) {
  const [drag, setDrag] = useState(false)
  return (
    <div
      className={`dropzone${drag ? ' over' : ''}`}
      onDragOver={(e) => {
        e.preventDefault()
        setDrag(true)
      }}
      onDragLeave={() => setDrag(false)}
      onDrop={(e) => {
        e.preventDefault()
        setDrag(false)
        const files = [...(e.dataTransfer?.files ?? [])]
        if (files.length) onFiles(files)
      }}
    >
      <label className="btn">
        {label}
        <input
          type="file"
          accept={accept}
          multiple
          disabled={disabled}
          style={{ display: 'none' }}
          onChange={(e) => {
            const files = [...(e.target.files ?? [])]
            if (files.length) onFiles(files)
            e.target.value = ''
          }}
        />
      </label>
      <span className="hint">or drop files here</span>
    </div>
  )
}

/// A dismissible error bar for when a page already has content but an action
/// failed. Keeps the page usable instead of replacing it with a bare error.
export function ErrorBar({ message, onDismiss }) {
  if (!message) return null
  return (
    <div className="error-bar">
      <span>{message}</span>
      <button className="btn" onClick={onDismiss}>
        Dismiss
      </button>
    </div>
  )
}

export { call }
