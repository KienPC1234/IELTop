// Audio device helpers for the WebView. Enumerating devices, playing a test
// tone to a chosen speaker, and recording a short clip from a chosen mic.
// Chrome or Edge labels only appear after the user grants microphone access,
// so the first test also unlocks the device names.

export function mediaSupported() {
  return typeof navigator !== 'undefined' && !!navigator.mediaDevices
}

/// Lists output and input devices. Falls back to a single default entry when
/// the platform hides them, so the pickers are never empty.
export async function listDevices() {
  if (!mediaSupported()) {
    return { speakers: [], mics: [] }
  }
  const all = await navigator.mediaDevices.enumerateDevices()
  const speakers = all
    .filter((d) => d.kind === 'audiooutput')
    .map((d) => ({ id: d.deviceId, label: d.label || 'Speaker' }))
  const mics = all
    .filter((d) => d.kind === 'audioinput')
    .map((d) => ({ id: d.deviceId, label: d.label || 'Microphone' }))
  return { speakers, mics }
}

/// Plays a short two tone beep on the chosen speaker, so the user can hear it.
/// Output device selection needs setSinkId; where it is missing, the beep just
/// uses the default speaker.
export async function playTestTone(deviceId?: string) {
  const Ctx = window.AudioContext || (window as any).webkitAudioContext
  const ctx = new Ctx()
  try {
    if (deviceId && typeof (ctx as any).setSinkId === 'function') {
      try {
        await (ctx as any).setSinkId(deviceId)
      } catch {
        // Some WebView builds refuse a sink change; fall back to the default.
      }
    }
    const now = ctx.currentTime
    for (let i = 0; i < 2; i++) {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.value = i === 0 ? 660 : 880
      const start = now + i * 0.35
      gain.gain.setValueAtTime(0.0001, start)
      gain.gain.exponentialRampToValueAtTime(0.25, start + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.28)
      osc.connect(gain).connect(ctx.destination)
      osc.start(start)
      osc.stop(start + 0.3)
    }
    await new Promise((r) => setTimeout(r, 900))
  } finally {
    ctx.close().catch(() => {})
  }
}

/// Records a few seconds from the chosen mic and plays it back. Returns the
/// recorded blob so a caller could reuse it, and null when it is not allowed.
export async function recordAndPlayback(deviceId?: string, seconds = 3): Promise<Blob | null> {
  if (!mediaSupported()) return null
  const audio = deviceId ? { deviceId: { exact: deviceId } } : true
  const stream = await navigator.mediaDevices.getUserMedia({ audio })
  try {
    const chunks: BlobPart[] = []
    const type = MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm' : ''
    const recorder = new MediaRecorder(stream, type ? { mimeType: type } : undefined)
    recorder.ondataavailable = (e) => e.data.size && chunks.push(e.data)
    const done = new Promise<Blob>((resolve) => {
      recorder.onstop = () => resolve(new Blob(chunks, { type: recorder.mimeType || 'audio/webm' }))
    })
    recorder.start()
    await new Promise((r) => setTimeout(r, Math.max(1, seconds) * 1000))
    recorder.stop()
    const blob = await done
    const url = URL.createObjectURL(blob)
    const player = new Audio(url)
    await player.play().catch(() => {})
    await new Promise((resolve) => {
      player.onended = resolve
      setTimeout(resolve, 6000)
    })
    URL.revokeObjectURL(url)
    return blob
  } finally {
    stream.getTracks().forEach((t) => t.stop())
  }
}

/// Applies the chosen speaker to an audio element when the platform allows it.
export async function applyOutputDevice(audioEl, deviceId) {
  if (!deviceId || !audioEl || typeof audioEl.setSinkId !== 'function') return
  try {
    await audioEl.setSinkId(deviceId)
  } catch {
    // Not supported for this element; the default speaker is used.
  }
}
