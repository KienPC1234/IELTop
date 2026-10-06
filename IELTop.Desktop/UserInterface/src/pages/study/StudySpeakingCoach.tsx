import { useState, useEffect, useRef } from 'react'
import {
  Mic, Square, Sparkles, Play, Pause, RotateCcw, Volume2,
  CheckCircle2, AlertTriangle, BookOpen, Clock, Award, ChevronRight,
  TrendingUp, RefreshCw, Plus, Trash2, History
} from 'lucide-react'
import { call } from '@/bridge'
import { blobToWavBase64 } from '@/audio'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Textarea } from '@/components/ui/textarea'
import { Input } from '@/components/ui/input'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ScrollArea } from '@/components/ui/scroll-area'

interface SpeakingCoachProps {
  onError?: (msg: string) => void
}

export default function StudySpeakingCoach({ onError }: SpeakingCoachProps) {
  const [snapshot, setSnapshot] = useState<any>(null)
  const [selectedPart, setSelectedPart] = useState<'Part1' | 'Part2' | 'Part3'>('Part2')
  const [customPrompt, setCustomPrompt] = useState('')
  const [topicSuggestion, setTopicSuggestion] = useState<any>(null)
  const [isRecording, setIsRecording] = useState(false)
  const [recordingSeconds, setRecordingSeconds] = useState(0)
  const [evaluating, setEvaluating] = useState(false)
  const [activeDrill, setActiveDrill] = useState<{ sentence: string; drill: any } | null>(null)
  const [drillLoading, setDrillLoading] = useState(false)
  const [audioPlayingUrl, setAudioPlayingUrl] = useState<string | null>(null)
  const [showHistory, setShowHistory] = useState(false)

  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const audioChunksRef = useRef<BlobPart[]>([])
  const timerIntervalRef = useRef<any>(null)
  const currentAudioElementRef = useRef<HTMLAudioElement | null>(null)

  const sid = snapshot?.selectedSessionId ?? 0
  const canUseAi = !!snapshot?.canUseAi
  const rubric = snapshot?.rubricResult
  const segments = snapshot?.segments ?? []
  const attempts = snapshot?.attempts ?? []

  async function loadSnapshot(targetSid = 0) {
    try {
      const res = await call('study.speaking.snapshot', {
        sessionId: targetSid,
        part: selectedPart,
        query: ''
      })
      setSnapshot(res)
    } catch (e: any) {
      onError?.(e.message)
    }
  }

  useEffect(() => {
    loadSnapshot()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedPart])

  useEffect(() => {
    return () => {
      if (timerIntervalRef.current) clearInterval(timerIntervalRef.current)
      if (currentAudioElementRef.current) {
        currentAudioElementRef.current.pause()
      }
    }
  }, [])

  async function handleSuggestTopic() {
    try {
      const res: any = await call('study.speaking.suggestTopic', { part: selectedPart })
      setTopicSuggestion(res)
      if (res?.prompt) {
        setCustomPrompt(res.prompt)
      }
    } catch (e: any) {
      onError?.(e.message)
    }
  }

  async function startRecording() {
    if (isRecording) return
    if (!navigator.mediaDevices?.getUserMedia) {
      onError?.('Microphone access is not supported in this environment.')
      return
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      audioChunksRef.current = []
      const recorder = new MediaRecorder(stream)

      recorder.ondataavailable = (e) => {
        if (e.data.size > 0) audioChunksRef.current.push(e.data)
      }

      mediaRecorderRef.current = recorder
      recorder.start(250)
      setIsRecording(true)
      setRecordingSeconds(0)

      timerIntervalRef.current = setInterval(() => {
        setRecordingSeconds((prev) => prev + 1)
      }, 1000)
    } catch (e: any) {
      onError?.(e.message || 'Microphone permission denied.')
    }
  }

  async function stopAndEvaluate() {
    if (!isRecording || !mediaRecorderRef.current) return

    clearInterval(timerIntervalRef.current)
    setIsRecording(false)
    setEvaluating(true)

    const recorder = mediaRecorderRef.current
    recorder.onstop = async () => {
      try {
        const audioBlob = new Blob(audioChunksRef.current, { type: recorder.mimeType || 'audio/webm' })
        const wavBase64 = await blobToWavBase64(audioBlob)

        if (!wavBase64) {
          throw new Error('Could not convert recording to WAV audio format.')
        }

        const cueText = customPrompt.trim() || topicSuggestion?.title || `${selectedPart} Practice Prompt`

        const res = await call('study.speaking.submitAnswer', {
          sessionId: sid,
          part: selectedPart,
          cue: cueText,
          audioBase64: wavBase64
        })

        setSnapshot(res)
      } catch (e: any) {
        onError?.(e.message || 'Evaluation failed. Check language model in Settings.')
      } finally {
        setEvaluating(false)
        setRecordingSeconds(0)
      }
    }

    recorder.stop()
    recorder.stream.getTracks().forEach((track) => track.stop())
  }

  function playSegmentAudio(audioBase64: string) {
    if (!audioBase64) return

    if (currentAudioElementRef.current) {
      currentAudioElementRef.current.pause()
    }

    const audioUrl = audioBase64.startsWith('data:')
      ? audioBase64
      : `data:audio/wav;base64,${audioBase64}`

    const audio = new Audio(audioUrl)
    currentAudioElementRef.current = audio
    setAudioPlayingUrl(audioUrl)

    audio.onended = () => setAudioPlayingUrl(null)
    audio.onerror = () => setAudioPlayingUrl(null)
    audio.play().catch(() => setAudioPlayingUrl(null))
  }

  async function handleLoadDrill(sentence: string, reason: string, drillType: string) {
    setDrillLoading(true)
    try {
      const res: any = await call('study.speaking.generateDrill', {
        sentence,
        reason,
        drillType
      })
      setActiveDrill({ sentence, drill: res })
    } catch (e: any) {
      onError?.(e.message)
    } finally {
      setDrillLoading(false)
    }
  }

  function formatTime(totalSec: number) {
    const mins = Math.floor(totalSec / 60)
    const secs = totalSec % 60
    return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`
  }

  return (
    <div className="flex flex-col gap-5">
      {/* Top Part Selector & Topic Setup */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b pb-4">
        <Tabs
          value={selectedPart}
          onValueChange={(val: any) => {
            setSelectedPart(val)
            setTopicSuggestion(null)
          }}
        >
          <TabsList className="grid grid-cols-3 w-[360px]">
            <TabsTrigger value="Part1">Part 1: Interview</TabsTrigger>
            <TabsTrigger value="Part2">Part 2: Long Turn</TabsTrigger>
            <TabsTrigger value="Part3">Part 3: Discussion</TabsTrigger>
          </TabsList>
        </Tabs>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={handleSuggestTopic}
            className="gap-1.5"
          >
            <Sparkles className="h-4 w-4 text-primary" />
            Suggest Topic
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setShowHistory(!showHistory)}
            className="gap-1.5"
          >
            <History className="h-4 w-4" />
            Attempts ({attempts.length})
          </Button>
        </div>
      </div>

      {/* Cue Card & Prompt Box */}
      <Card className="border-border">
        <CardHeader className="pb-3">
          <div className="flex items-center justify-between">
            <CardTitle className="text-base font-semibold flex items-center gap-2">
              <BookOpen className="h-4 w-4 text-primary" />
              {topicSuggestion?.title || `${selectedPart} Speaking Topic`}
            </CardTitle>
            <Badge variant="secondary" className="font-mono text-xs">
              {selectedPart === 'Part2' ? 'Target: 1.5 to 2.0 mins' : 'Target: 30 to 45 secs'}
            </Badge>
          </div>
          <CardDescription className="text-xs">
            Review the topic card, use suggested academic phrases, and record your response for 4-criteria IELTS evaluation.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <Textarea
            value={customPrompt}
            onChange={(e) => setCustomPrompt(e.target.value)}
            placeholder="Enter custom prompt, paste a cue card, or click 'Suggest Topic'..."
            rows={selectedPart === 'Part2' ? 3 : 2}
            className="text-sm font-medium resize-none"
          />

          {topicSuggestion?.bullets && topicSuggestion.bullets.length > 0 && (
            <div className="rounded-md bg-muted/40 p-3 text-xs flex flex-col gap-1 border">
              <span className="font-semibold text-muted-foreground">You should say:</span>
              <ul className="list-disc list-inside space-y-0.5 text-foreground">
                {topicSuggestion.bullets.map((b: string, i: number) => (
                  <li key={i}>{b}</li>
                ))}
              </ul>
            </div>
          )}

          {topicSuggestion?.usefulVocab && topicSuggestion.usefulVocab.length > 0 && (
            <div className="flex flex-wrap items-center gap-1.5 pt-1">
              <span className="text-xs text-muted-foreground mr-1">Recommended collocations:</span>
              {topicSuggestion.usefulVocab.map((v: string, i: number) => (
                <Badge key={i} variant="outline" className="text-xs font-normal">
                  {v}
                </Badge>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Recording Control Center */}
      <div className="flex flex-col items-center justify-center p-6 border rounded-lg bg-card gap-4">
        <div className="flex items-center gap-3">
          {isRecording ? (
            <div className="flex items-center gap-2 px-3 py-1.5 rounded-full bg-destructive/10 text-destructive text-sm font-semibold animate-pulse border border-destructive/20">
              <span className="h-2.5 w-2.5 rounded-full bg-destructive animate-ping" />
              Recording: {formatTime(recordingSeconds)}
            </div>
          ) : (
            <div className="text-xs text-muted-foreground flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5" />
              Ready to record. Click button when you are prepared to speak.
            </div>
          )}
        </div>

        <div className="flex items-center gap-3">
          {!isRecording ? (
            <Button
              size="lg"
              disabled={evaluating}
              onClick={startRecording}
              className="gap-2 px-6 font-semibold"
            >
              <Mic className="h-5 w-5" />
              Start Speaking
            </Button>
          ) : (
            <Button
              size="lg"
              variant="destructive"
              onClick={stopAndEvaluate}
              className="gap-2 px-6 font-semibold"
            >
              <Square className="h-5 w-5" />
              Stop and Evaluate
            </Button>
          )}
        </div>

        {evaluating && (
          <div className="flex items-center gap-2 text-xs text-muted-foreground animate-pulse">
            <RefreshCw className="h-3.5 w-3.5 animate-spin text-primary" />
            Analyzing speech with speech-to-text, slicing audio sentences, and evaluating with IELTS band descriptors...
          </div>
        )}
      </div>

      {/* Rubric Evaluation Results */}
      {rubric && (
        <div className="flex flex-col gap-4">
          <div className="flex items-center justify-between border-b pb-2">
            <div className="flex items-center gap-2">
              <Award className="h-5 w-5 text-primary" />
              <h3 className="font-semibold text-base">IELTS Official Rubric Assessment</h3>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">Estimated Overall:</span>
              <Badge className="text-sm px-2.5 py-0.5 font-bold">
                Band {rubric.overallBand.toFixed(1)}
              </Badge>
            </div>
          </div>

          {/* 4 Criteria Cards */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <Card className="border-border">
              <CardHeader className="p-3 pb-2">
                <div className="flex justify-between items-center">
                  <span className="text-xs font-semibold">Fluency & Coherence (FC)</span>
                  <Badge variant="outline" className="font-mono font-bold">
                    Band {rubric.fcBand.toFixed(1)}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="p-3 pt-0 text-xs text-muted-foreground">
                {rubric.fcFeedback}
              </CardContent>
            </Card>

            <Card className="border-border">
              <CardHeader className="p-3 pb-2">
                <div className="flex justify-between items-center">
                  <span className="text-xs font-semibold">Lexical Resource (LR)</span>
                  <Badge variant="outline" className="font-mono font-bold">
                    Band {rubric.lrBand.toFixed(1)}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="p-3 pt-0 text-xs text-muted-foreground">
                {rubric.lrFeedback}
              </CardContent>
            </Card>

            <Card className="border-border">
              <CardHeader className="p-3 pb-2">
                <div className="flex justify-between items-center">
                  <span className="text-xs font-semibold">Grammatical Range & Accuracy (GRA)</span>
                  <Badge variant="outline" className="font-mono font-bold">
                    Band {rubric.graBand.toFixed(1)}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="p-3 pt-0 text-xs text-muted-foreground">
                {rubric.graFeedback}
              </CardContent>
            </Card>

            <Card className="border-border">
              <CardHeader className="p-3 pb-2">
                <div className="flex justify-between items-center">
                  <span className="text-xs font-semibold">Pronunciation (PR)</span>
                  <Badge variant="outline" className="font-mono font-bold">
                    Band {rubric.prBand.toFixed(1)}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="p-3 pt-0 text-xs text-muted-foreground">
                {rubric.prFeedback}
              </CardContent>
            </Card>
          </div>

          {/* Upgrades and Key Errors */}
          {rubric.upgrades && rubric.upgrades.length > 0 && (
            <Card className="border-border">
              <CardHeader className="p-3 pb-2">
                <CardTitle className="text-xs font-semibold flex items-center gap-1.5 text-primary">
                  <TrendingUp className="h-3.5 w-3.5" />
                  Vocabulary Upgrades (Band 8+ Collocations)
                </CardTitle>
              </CardHeader>
              <CardContent className="p-3 pt-0">
                <div className="divide-y divide-border text-xs">
                  {rubric.upgrades.map((u: any, idx: number) => (
                    <div key={idx} className="py-2 flex flex-col gap-0.5">
                      <div className="flex items-center gap-2">
                        <span className="line-through text-muted-foreground">{u.original}</span>
                        <ChevronRight className="h-3 w-3 text-muted-foreground" />
                        <span className="font-semibold text-primary">{u.upgraded}</span>
                      </div>
                      <span className="text-muted-foreground text-[11px]">{u.note}</span>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          )}

          {rubric.keyErrors && rubric.keyErrors.length > 0 && (
            <Card className="border-border">
              <CardHeader className="p-3 pb-2">
                <CardTitle className="text-xs font-semibold flex items-center gap-1.5 text-destructive">
                  <AlertTriangle className="h-3.5 w-3.5" />
                  Key Grammar & Usage Errors
                </CardTitle>
              </CardHeader>
              <CardContent className="p-3 pt-0">
                <div className="divide-y divide-border text-xs">
                  {rubric.keyErrors.map((e: any, idx: number) => (
                    <div key={idx} className="py-2 flex flex-col gap-0.5">
                      <div className="flex items-center gap-2">
                        <span className="text-destructive font-medium">"{e.quote}"</span>
                        <ChevronRight className="h-3 w-3 text-muted-foreground" />
                        <span className="font-semibold text-foreground">{e.correction}</span>
                      </div>
                      <span className="text-muted-foreground text-[11px]">{e.reason}</span>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          )}
        </div>
      )}

      {/* Sliced Audio Sentence Drill Practice */}
      {segments && segments.length > 0 && (
        <div className="flex flex-col gap-3 pt-2">
          <div className="flex items-center justify-between border-b pb-2">
            <h3 className="font-semibold text-base flex items-center gap-2">
              <Volume2 className="h-4 w-4 text-primary" />
              Audio Sentence Segments & Shadowing Drills
            </h3>
            <span className="text-xs text-muted-foreground">
              {segments.length} sliced sentence(s)
            </span>
          </div>

          <div className="flex flex-col gap-2.5">
            {segments.map((seg: any, idx: number) => {
              const hasAudio = !!seg.audioBase64
              const isPlaying = audioPlayingUrl && audioPlayingUrl.includes(seg.audioBase64?.slice(0, 30))
              const isFlagged = !!seg.flaggedIssue

              return (
                <div
                  key={idx}
                  className={`border rounded-lg p-3 flex flex-col gap-2 transition-colors ${
                    isFlagged ? 'bg-amber-500/5 border-amber-500/30' : 'bg-card'
                  }`}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex items-start gap-2">
                      <Button
                        size="icon"
                        variant={isPlaying ? 'default' : 'outline'}
                        className="h-7 w-7 shrink-0 rounded-full"
                        disabled={!hasAudio}
                        onClick={() => playSegmentAudio(seg.audioBase64)}
                        title="Listen to isolated sentence recording"
                      >
                        {isPlaying ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
                      </Button>
                      <div className="flex flex-col gap-1">
                        <span className="text-xs font-medium text-foreground">
                          {seg.sentenceText}
                        </span>
                        {seg.flaggedIssue && (
                          <span className="text-[11px] text-amber-600 dark:text-amber-400 font-medium">
                            Flagged: {seg.flaggedIssue}
                          </span>
                        )}
                      </div>
                    </div>

                    <div className="flex items-center gap-1.5 shrink-0">
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-7 text-xs px-2"
                        disabled={drillLoading}
                        onClick={() => handleLoadDrill(seg.sentenceText, seg.flaggedIssue, 'shadowing')}
                      >
                        Shadow
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        className="h-7 text-xs px-2"
                        disabled={drillLoading}
                        onClick={() => handleLoadDrill(seg.sentenceText, seg.flaggedIssue, 'upgrade')}
                      >
                        Upgrade
                      </Button>
                    </div>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Interactive Micro Drill Modal / Card */}
      {activeDrill && (
        <Card className="border-primary/40 bg-card">
          <CardHeader className="p-4 pb-2">
            <div className="flex justify-between items-center">
              <CardTitle className="text-sm font-semibold flex items-center gap-1.5">
                <Sparkles className="h-4 w-4 text-primary" />
                Targeted Practice Drill
              </CardTitle>
              <Button
                variant="ghost"
                size="sm"
                className="h-6 w-6 p-0 text-muted-foreground"
                onClick={() => setActiveDrill(null)}
              >
                ✕
              </Button>
            </div>
            <CardDescription className="text-xs font-medium text-foreground">
              Original: "{activeDrill.sentence}"
            </CardDescription>
          </CardHeader>
          <CardContent className="p-4 pt-1 flex flex-col gap-2.5 text-xs">
            {activeDrill.drill?.upgraded && (
              <div className="rounded bg-primary/10 p-2.5 text-foreground font-medium">
                <span className="text-primary font-semibold mr-1.5">Model Answer:</span>
                {activeDrill.drill.upgraded}
              </div>
            )}
            {activeDrill.drill?.collocations && (
              <div className="text-muted-foreground">
                <span className="font-semibold text-foreground">Key collocations:</span>{' '}
                {activeDrill.drill.collocations.join(', ')}
              </div>
            )}
            {activeDrill.drill?.tip && (
              <div className="text-muted-foreground italic">
                Tip: {activeDrill.drill.tip}
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  )
}
