/// Writing Task 1 and Task 2: the prompt, the chart when there is one, and the
/// essay box with a live word count. Not auto scored; AI marking is on submit.
export default function WritingPanel({ part, busy, runCall }) {
  return (
    <div className="writing">
      <h3>{part.title}</h3>
      {part.hasWritingTask1Image && part.hasWritingImageFile && (
        <figure className="chart">
          <img src={part.imageUrl} alt="Task 1 chart" />
          <figcaption>{part.imageHint}</figcaption>
        </figure>
      )}
      {part.showMissingImageHint && <div className="warn">{part.imageHint}</div>}

      {part.material && <div className="prompt-text">{part.material}</div>}
      {part.instructions && <div className="hint">{part.instructions}</div>}

      <div className="field">
        <label>Your answer</label>
        <textarea
          className="essay"
          rows={16}
          value={part.essay}
          placeholder="Write your essay here."
          onChange={(e) => runCall('exam.setEssay', { text: e.target.value })}
        />
      </div>
      <div className="hint">{part.wordCountLabel}</div>
      <p className="hint">
        Writing is not auto scored. It is marked by AI after submit, on the
        official four criteria.
      </p>
    </div>
  )
}
