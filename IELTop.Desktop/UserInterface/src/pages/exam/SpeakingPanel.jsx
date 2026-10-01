/// Speaking: Step 1 read the cue, Step 2 record, Step 3 fix the transcript,
/// Step 4 finish. Recording runs without pause, like the real test.
export default function SpeakingPanel({ part, busy, runCall }) {
  return (
    <div className="speaking">
      <h3>{part.title}</h3>
      {part.speakingCue && <div className="cue-card">{part.speakingCue}</div>}
      {part.instructions && <div className="hint">{part.instructions}</div>}

      <ol className="steps">
        <li className={part.isRecording ? 'on' : ''}>{part.speakingStepLabel}</li>
      </ol>

      <div className="record-row">
        <button
          className="btn primary"
          disabled={busy || part.isRecording}
          onClick={() => runCall('exam.beginRecording')}
        >
          {part.isRecording ? 'Recording...' : 'Record'}
        </button>
        {part.isRecording && <span className="recording">Speak now, no pause.</span>}
      </div>

      <div className="hint">{part.audioStatus}</div>
      <div className="hint">{part.recordingHint}</div>

      <div className="field">
        <label>What you said (transcript)</label>
        <textarea
          className="essay"
          rows={7}
          value={part.transcript}
          placeholder="The transcript fills in when a speech model is installed. You can also type it."
          onChange={(e) => runCall('exam.setTranscript', { text: e.target.value })}
        />
      </div>
      <div className="hint">{part.transcriptWordCount}</div>

      <div className="actions">
        <button className="btn" disabled={busy} onClick={() => runCall('exam.checkPronunciation')}>
          Check pronunciation
        </button>
        <button className="btn primary" disabled={busy} onClick={() => runCall('exam.finishSpeaking')}>
          Finish part
        </button>
      </div>

      {part.pronunciationSummary && (
        <div className="pronunciation">
          <div className="hint">{part.pronunciationSummary}</div>
          {part.pronunciationWords?.length > 0 && (
            <table className="data-table">
              <thead>
                <tr>
                  <th>Word</th>
                  <th>Expected</th>
                  <th>Heard</th>
                </tr>
              </thead>
              <tbody>
                {part.pronunciationWords.map((w, i) => (
                  <tr key={i}>
                    <td>{w.word}</td>
                    <td className="mono">{w.expected}</td>
                    <td className="mono">{w.heard}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="hint">
            Pronunciation match is a practice estimate from the offline model, not an
            official score.
          </p>
        </div>
      )}
    </div>
  )
}
