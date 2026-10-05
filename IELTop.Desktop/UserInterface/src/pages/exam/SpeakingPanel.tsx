import { useEffect, useRef, useState } from 'react'
import { Mic, CheckCircle2, AlertCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Field } from '@/components/Field'
import { useDraftText } from '@/hooks'
import { recordAndPlayback } from '@/audio'

/// Speaking. In test mode (the default) the flow is strict: check the microphone
/// first, then the recording runs once and is submitted and scored straight away.
/// The transcript is what the speech model heard, so it is read only. When the
/// setting is off, the transcript box stays for practice.
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
  const [mic, setMic] = useState<'unknown' | 'testing' | 'ok' | 'fail'>('unknown')
  const [micError, setMicError] = useState('')
  const [transcript, setTranscript, flushTranscript] = useDraftText(
    part.transcript ?? '',
    part.index,
    (text) => runCall('exam.setTranscript', { text })
  )

  // A new part needs its own microphone check.
  useEffect(() => {
    setMic('unknown')
    setMicError('')
  }, [part.index])

  async function testMic() {
    setMic('testing')
    setMicError('')
    try {
      const blob = await recordAndPlayback(undefined, 3)
      if (blob && blob.size > 0) setMic('ok')
      else {
        setMic('fail')
        setMicError('No sound was captured. Check the microphone and try again.')
      }
    } catch {
      setMic('fail')
      setMicError('The microphone could not be opened. Allow access and try again.')
    }
  }

  const canRecord = !auto || mic === 'ok'

  return (
    <div className="w-full flex flex-col gap-4">
      {/* Instruction block */}
      <div className="border border-exam-block-border border-l-4 border-l-exam-instruction-accent bg-exam-instruction p-4 select-none">
        <h2 className="text-base font-bold text-foreground">
          {part.instructionHeading || `Part ${part.skillPartNumber || part.index + 1}`}
        </h2>
        <p className="mt-0.5 text-sm text-foreground/80 leading-relaxed">
          {part.bannerInstruction || part.instructions || 'Answer the questions clearly and fluently.'}
        </p>
      </div>

      <div>
        <h3 className="mb-2 text-base font-bold text-foreground">{part.title}</h3>
        {part.speakingCue && (
          <div className="mb-3 border border-exam-block-border border-l-4 border-l-exam-instruction-accent bg-exam-instruction p-4 leading-relaxed">
            {part.speakingCue}
          </div>
        )}
        {part.instructions && <p className="mb-3 text-sm text-muted-foreground">{part.instructions}</p>}

        <p className={`mb-3 text-sm ${part.isRecording ? 'font-semibold text-primary' : 'text-muted-foreground'}`}>
          {part.speakingStepLabel}
        </p>

        {/* Microphone check comes first in test mode, so a silent mic is caught
            before the answer is recorded. */}
        {auto && (
          <div className="mb-4 border border-exam-block-border bg-exam-surface p-3.5">
            <div className="flex flex-wrap items-center gap-3">
              <Button
                variant="outline"
                className="rounded-none gap-1.5"
                disabled={busy || part.isRecording || mic === 'testing'}
                onClick={testMic}
              >
                <Mic className="h-4 w-4" />
                {mic === 'testing' ? 'Testing...' : 'Test microphone'}
              </Button>
              {mic === 'ok' && (
                <span className="flex items-center gap-1.5 text-sm text-success">
                  <CheckCircle2 className="h-4 w-4" /> Microphone works. You can record.
                </span>
              )}
              {mic === 'fail' && (
                <span className="flex items-center gap-1.5 text-sm text-destructive">
                  <AlertCircle className="h-4 w-4" /> {micError}
                </span>
              )}
              {mic === 'unknown' && (
                <span className="text-sm text-muted-foreground">
                  Test the microphone before you record.
                </span>
              )}
            </div>
          </div>
        )}

        <div className="mb-3 flex flex-wrap items-center gap-3">
          <Button
            className="rounded-none"
            disabled={busy || part.isRecording || !canRecord}
            title={canRecord ? '' : 'Test the microphone first.'}
            onClick={() => runCall('exam.beginRecording')}
          >
            {part.isRecording ? 'Recording...' : 'Record'}
          </Button>
          {part.isRecording && <span className="text-sm font-semibold text-primary">Speak now, no pause.</span>}
        </div>

        <p className="text-sm text-muted-foreground">{part.audioStatus}</p>
        <p className="mb-3 text-sm text-muted-foreground">{part.recordingHint}</p>
        {part.speechTimingLabel && (
          <p className="mb-3 text-sm text-muted-foreground">{part.speechTimingLabel}</p>
        )}

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
