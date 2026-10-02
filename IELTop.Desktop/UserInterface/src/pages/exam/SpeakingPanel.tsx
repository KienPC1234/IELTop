import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Field } from '@/components/Field'
import { useDraftText } from '@/hooks'

/// Speaking: Step 1 read the cue, Step 2 record, Step 3 fix the transcript,
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
  // Typing only commits after a pause, so the transcript box never lags.
  const [transcript, setTranscript, flushTranscript] = useDraftText(
    part.transcript ?? '',
    part.index,
    (text) => runCall('exam.setTranscript', { text })
  )
  return (
    <div className="max-w-[900px]">
      <h3 className="mb-2 font-semibold">{part.title}</h3>
      {part.speakingCue && (
        <div className="mb-3 rounded-none border border-border border-l-4 border-l-primary bg-muted/50 p-3 leading-relaxed">
          {part.speakingCue}
        </div>
      )}
      {part.instructions && <p className="mb-3 text-sm text-muted-foreground">{part.instructions}</p>}

      <p className={`mb-3 text-sm ${part.isRecording ? 'font-semibold text-primary' : 'text-muted-foreground'}`}>
        {part.speakingStepLabel}
      </p>

      <div className="mb-3 flex flex-wrap items-center gap-3">
        <Button disabled={busy || part.isRecording} onClick={() => runCall('exam.beginRecording')}>
          {part.isRecording ? 'Recording...' : 'Record'}
        </Button>
        {part.isRecording && <span className="text-sm font-semibold text-primary">Speak now, no pause.</span>}
      </div>

      <p className="text-sm text-muted-foreground">{part.audioStatus}</p>
      <p className="mb-3 text-sm text-muted-foreground">{part.recordingHint}</p>

      <Field label="What you said (transcript)">
        <textarea
          className="min-h-[160px] w-full rounded-none border border-input bg-background p-3 align-top text-[15px] leading-relaxed"
          value={transcript}
          placeholder="The transcript fills in when a speech model is installed. You can also type it."
          onChange={(e) => setTranscript(e.currentTarget.value)}
          onBlur={flushTranscript}
        />
      </Field>
      <p className="text-sm text-muted-foreground">{part.transcriptWordCount}</p>

      <div className="mt-3 flex flex-wrap gap-2">
        <Button variant="outline" disabled={busy} onClick={() => runCall('exam.checkPronunciation')}>Check pronunciation</Button>
        <Button disabled={busy} onClick={() => runCall('exam.finishSpeaking')}>Finish part</Button>
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
                </TableRow>
              </TableHeader>
              <TableBody>
                {part.pronunciationWords.map((w, i) => (
                  <TableRow key={i}>
                    <TableCell>{w.word}</TableCell>
                    <TableCell className="font-mono">{w.expected}</TableCell>
                    <TableCell className="font-mono">{w.heard}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          <p className="mt-2 text-sm text-muted-foreground">
            Pronunciation match is a practice estimate from the offline model, not an official score.
          </p>
        </div>
      )}
    </div>
  )
}
