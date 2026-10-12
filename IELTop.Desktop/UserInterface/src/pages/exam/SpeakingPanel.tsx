import { useEffect, useState } from 'react'
import { Mic, Square, Radio, Settings2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Field } from '@/components/Field'
import { useDraftText } from '@/hooks'
import { listDevices } from '@/audio'

import { call } from '@/bridge'

/// Speaking panel. Clear prompt cue card, straightforward recording and submission,
/// with device switching available without blocking the candidate.
export default function SpeakingPanel({
  part,
  busy,
  runCall,
  run,
}: {
  part: any
  busy?: boolean
  runCall: (method: string, args?: any) => Promise<any>
  run?: any
}) {
  const auto = (run?.speakingAutoSubmit ?? part.speakingAutoSubmit) !== false
  const [mics, setMics] = useState<{ id: string; label: string }[]>([])
  const [selectedMic, setSelectedMic] = useState<string>('__default')
  const [showMicPicker, setShowMicPicker] = useState(false)
  const [transcript, setTranscript, flushTranscript] = useDraftText(
    part.transcript ?? '',
    part.index,
    (text) => runCall('exam.setTranscript', { text })
  )

  useEffect(() => {
    listDevices()
      .then((d) => setMics(d.mics || []))
      .catch(() => {})
    call('settings.snapshot')
      .then((s: any) => {
        if (s?.audioInputDeviceId) setSelectedMic(s.audioInputDeviceId)
      })
      .catch(() => {})
  }, [])

  function handleStopRecording() {
    if (typeof (window as any).__ieltop_stop_recording === 'function') {
      ;(window as any).__ieltop_stop_recording()
    }
  }

  async function handleSwitchMic(deviceId: string) {
    setSelectedMic(deviceId)
    const val = deviceId === '__default' ? '' : deviceId
    try {
      await call('settings.setAudioInput', { value: val })
    } catch {
      // fallback
    }
  }

  const isTranscribing = Boolean(
    part?.audioStatus?.includes('Transcribing') ||
    part?.audioStatus?.includes('Getting the transcription')
  )

  return (
    <div className="w-full flex flex-col gap-4">
      {/* Instruction block */}
      <div className="border border-exam-block-border border-l-4 border-l-exam-instruction-accent bg-exam-instruction p-4 select-none">
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-foreground">
            {part.instructionHeading || `Part ${part.skillPartNumber || part.index + 1}`}
          </h2>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => setShowMicPicker(!showMicPicker)}
            className="h-7 text-xs gap-1.5 text-muted-foreground hover:text-foreground rounded-none"
            title="Switch microphone"
          >
            <Settings2 className="h-3.5 w-3.5" />
            Microphone
          </Button>
        </div>
        <p className="mt-0.5 text-sm text-foreground/80 leading-relaxed">
          {part.bannerInstruction || part.instructions || 'Answer the questions clearly and fluently.'}
        </p>

        {showMicPicker && (
          <div className="mt-3 pt-3 border-t border-exam-block-border flex items-center gap-3">
            <span className="text-xs font-medium text-foreground">Change input:</span>
            <Select value={selectedMic} onValueChange={handleSwitchMic}>
              <SelectTrigger className="h-8 text-xs rounded-none bg-exam-surface w-64">
                <SelectValue placeholder="System Default" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__default">System Default Microphone</SelectItem>
                {mics.map((m) => (
                  <SelectItem key={m.id} value={m.id} className="text-xs">
                    {m.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
      </div>

      <div>
        <h3 className="mb-2 text-base font-bold text-foreground">{part.title}</h3>
        {part.speakingCue && (
          <div className="mb-4 border border-exam-block-border border-l-4 border-l-exam-instruction-accent bg-exam-instruction p-4 text-sm font-medium leading-relaxed whitespace-pre-wrap">
            {part.speakingCue}
          </div>
        )}
        {part.instructions && <p className="mb-3 text-sm text-muted-foreground">{part.instructions}</p>}

        {/* Clean recording action bar */}
        <div className="mb-4 border border-exam-block-border bg-exam-surface p-4 flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-3">
            {!part.isRecording ? (
              <Button
                className="rounded-none gap-2 font-semibold min-w-36"
                disabled={busy || isTranscribing}
                onClick={() => runCall('exam.beginRecording')}
              >
                <Mic className="h-4 w-4" />
                {isTranscribing ? 'Transcribing...' : 'Record Response'}
              </Button>
            ) : (
              <Button
                variant="destructive"
                className="rounded-none gap-2 font-semibold min-w-44 animate-pulse"
                disabled={busy}
                onClick={handleStopRecording}
              >
                <Square className="h-4 w-4 fill-current" />
                Stop & Submit Response
              </Button>
            )}

            {part.isRecording ? (
              <div className="flex items-center gap-2 text-sm font-semibold text-destructive">
                <Radio className="h-4 w-4 animate-ping" />
                <span>Recording in progress... Speak clearly now.</span>
              </div>
            ) : (
              <span className="text-sm text-muted-foreground">
                {part.audioStatus || 'Click Record Response when ready to speak.'}
              </span>
            )}
          </div>

          {part.recordingHint && (
            <p className="text-xs text-muted-foreground">{part.recordingHint}</p>
          )}
          {part.speechTimingLabel && (
            <p className="text-xs font-mono text-muted-foreground">{part.speechTimingLabel}</p>
          )}
        </div>

        {auto ? (
          <Field label="What the model heard (read only)">
            <textarea
              readOnly
              className="min-h-[140px] w-full rounded-none border border-input bg-exam-surface p-3.5 align-top text-sm leading-relaxed"
              value={part.transcript ?? ''}
              placeholder="Filled from your recording after you stop. In test mode it is not edited, so the score matches what you said."
            />
          </Field>
        ) : (
          <Field label="What you said (transcript)">
            <textarea
              className="min-h-[160px] w-full rounded-none border border-input bg-exam-block p-3.5 align-top text-sm leading-relaxed focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              value={transcript}
              placeholder="The transcript fills in when a speech model is installed. You can also type it."
              onChange={(e) => setTranscript(e.currentTarget.value)}
              onBlur={flushTranscript}
            />
          </Field>
        )}
        <p className="mt-1 text-xs text-muted-foreground">{part.transcriptWordCount}</p>

        <div className="mt-3 flex flex-wrap gap-2">
          <Button variant="outline" className="rounded-none" disabled={busy} onClick={() => runCall('exam.checkPronunciation')}>
            Check pronunciation
          </Button>
          {auto ? (
            <span className="self-center text-sm text-muted-foreground">
              Recording once submits and scores this part on its own.
            </span>
          ) : (
            <Button className="rounded-none" disabled={busy} onClick={() => runCall('exam.finishSpeaking')}>
              Finish part
            </Button>
          )}
        </div>

        {part.pronunciationSummary && (
          <div className="mt-4 border-t border-border pt-3">
            <p className="text-sm text-muted-foreground">{part.pronunciationSummary}</p>
            {part.pronunciationWords?.length > 0 && (
              <Table className="mt-2">
                <TableHeader>
                  <TableRow>
                    <TableHead>Word</TableHead>
                    <TableHead>Expected</TableHead>
                    <TableHead>Heard</TableHead>
                    <TableHead className="text-right">Sure</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {part.pronunciationWords.map((w: any, i: number) => (
                    <TableRow key={i}>
                      <TableCell>{w.word}</TableCell>
                      <TableCell className="font-mono">{w.expected}</TableCell>
                      <TableCell className="font-mono">{w.heard}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {typeof w.gop === 'number' ? `${Math.round(w.gop * 100)}%` : ''}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
            <p className="mt-2 text-xs text-muted-foreground">
              Pronunciation match is a practice estimate from the offline model, not an official score.
            </p>
          </div>
        )}
      </div>
    </div>
  )
}
