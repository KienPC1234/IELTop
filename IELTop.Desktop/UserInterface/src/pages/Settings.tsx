import { useEffect, useState } from 'react'
import {
  Bot,
  Volume2,
  Mic,
  Palette,
  HardDrive,
  Activity,
  Info,
  CheckCircle2,
  AlertCircle,
  Eye,
  EyeOff,
  RefreshCw,
  FolderOpen,
  ExternalLink,
  Shield,
  RotateCcw,
  Sliders,
  Database,
  Radio,
  Sparkles,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Switch } from '@/components/ui/switch'
import { Badge } from '@/components/ui/badge'
import { Separator } from '@/components/ui/separator'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { usePage } from '@/hooks'
import { call } from '@/bridge'
import { Confirm, ErrorBar, PageHeader } from '@/components/shared'
import { Field } from '@/components/Field'
import { ThemeToggle } from '@/components/ThemeToggle'
import DiagnosticsPanel from '@/pages/settings/DiagnosticsPanel'
import { listDevices, playTestTone, recordAndPlayback, mediaSupported } from '@/audio'

export default function Settings() {
  const page = usePage('settings.snapshot')
  const [confirm, setConfirm] = useState(null)
  const [models, setModels] = useState(null)
  const [devices, setDevices] = useState({ speakers: [], mics: [] })
  const [audioBusy, setAudioBusy] = useState('')
  const [audioNote, setAudioNote] = useState('')
  const [showApiKey, setShowApiKey] = useState(false)
  const [activeTab, setActiveTab] = useState('ai')

  const d = page.data

  useEffect(() => {
    call('settings.models').then(setModels).catch(() => setModels([]))
  }, [])

  async function refreshDevices(unlock = false) {
    try {
      if (unlock && mediaSupported()) {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
        stream.getTracks().forEach((t) => t.stop())
      }
      setDevices(await listDevices())
    } catch {
      setDevices({ speakers: [], mics: [] })
    }
  }

  useEffect(() => {
    refreshDevices(false)
  }, [])

  if (page.loading && !d) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted-foreground">
        Loading settings...
      </div>
    )
  }

  if (page.error && !d) {
    return <div className="p-4 text-sm text-destructive">{page.error}</div>
  }

  if (!d) return null

  const run = page.run
  const set = (method, value) => run(method, { value })

  return (
    <div className="flex flex-col gap-6 pb-12">
      <ErrorBar message={page.error} onDismiss={() => page.setError('')} />

      <PageHeader
        title="Settings"
        description="Configure offline AI models, audio hardware, appearance, and exam environment."
      />

      <Tabs value={activeTab} onValueChange={setActiveTab} className="w-full">
        <TabsList className="grid h-11 w-full grid-cols-6 p-1 bg-muted/70">
          <TabsTrigger value="ai" className="flex items-center gap-2 text-xs sm:text-sm">
            <Bot className="h-4 w-4" />
            <span>AI & Models</span>
          </TabsTrigger>
          <TabsTrigger value="audio" className="flex items-center gap-2 text-xs sm:text-sm">
            <Volume2 className="h-4 w-4" />
            <span>Sound & Mic</span>
          </TabsTrigger>
          <TabsTrigger value="appearance" className="flex items-center gap-2 text-xs sm:text-sm">
            <Palette className="h-4 w-4" />
            <span>Appearance</span>
          </TabsTrigger>
          <TabsTrigger value="offline" className="flex items-center gap-2 text-xs sm:text-sm">
            <HardDrive className="h-4 w-4" />
            <span>Offline Models</span>
          </TabsTrigger>
          <TabsTrigger value="diagnostics" className="flex items-center gap-2 text-xs sm:text-sm">
            <Activity className="h-4 w-4" />
            <span>Diagnostics</span>
          </TabsTrigger>
          <TabsTrigger value="about" className="flex items-center gap-2 text-xs sm:text-sm">
            <Info className="h-4 w-4" />
            <span>About & Updates</span>
          </TabsTrigger>
        </TabsList>

        {/* Tab 1: AI & Models */}
        <TabsContent value="ai" className="mt-4 flex flex-col gap-5">
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between">
                <div>
                  <CardTitle className="text-base font-semibold">OpenAI Compatible Chat API</CardTitle>
                  <CardDescription>
                    Connect any OpenAI API compatible provider (OpenAI, Ollama, LM Studio, vLLM, llama.cpp).
                  </CardDescription>
                </div>
                <Badge variant={d.isValid ? 'secondary' : 'outline'} className="gap-1 text-xs">
                  {d.isValid ? (
                    <>
                      <CheckCircle2 className="h-3 w-3 text-success" /> Ready
                    </>
                  ) : (
                    <>
                      <AlertCircle className="h-3 w-3 text-warning" /> Needs configuration
                    </>
                  )}
                </Badge>
              </div>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <Field label="Base URL" htmlFor="base-url">
                  <Input
                    id="base-url"
                    placeholder="https://api.openai.com/v1"
                    value={d.baseUrl}
                    onChange={(e) => set('settings.setBaseUrl', e.target.value)}
                  />
                </Field>

                <Field label="Model name" htmlFor="model">
                  <Input
                    id="model"
                    placeholder="gpt-4o-mini, llama3.2, qwen2.5..."
                    value={d.model}
                    onChange={(e) => set('settings.setModel', e.target.value)}
                  />
                </Field>

                <Field label="API key (stored with local machine encryption)" htmlFor="api-key" className="md:col-span-2">
                  <div className="relative">
                    <Input
                      id="api-key"
                      type={showApiKey ? 'text' : 'password'}
                      placeholder={d.hasApiKey ? 'Key is securely saved' : 'sk-... (leave empty for local Ollama)'}
                      value={d.apiKey}
                      onChange={(e) => set('settings.setApiKey', e.target.value)}
                      className="pr-10"
                    />
                    <button
                      type="button"
                      onClick={() => setShowApiKey(!showApiKey)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                      aria-label="Toggle API key visibility"
                    >
                      {showApiKey ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </Field>
              </div>

              <Separator className="my-1" />

              <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                <Field label="Temperature" htmlFor="temp">
                  <Input
                    id="temp"
                    type="number"
                    step="0.1"
                    min="0"
                    max="2"
                    value={d.temperature}
                    onChange={(e) => set('settings.setTemperature', Number(e.target.value))}
                  />
                </Field>

                <Field label="Max tokens" htmlFor="max-tokens">
                  <div className="space-y-1.5">
                    <Input
                      id="max-tokens"
                      type="number"
                      min="64"
                      max="65536"
                      step="512"
                      value={d.maxTokens}
                      onChange={(e) => set('settings.setMaxTokens', Number(e.target.value))}
                    />
                    <div className="flex flex-wrap gap-1">
                      {[2048, 4096, 8192, 16384, 32768].map((tok) => (
                        <button
                          key={tok}
                          type="button"
                          onClick={() => set('settings.setMaxTokens', tok)}
                          className={`rounded px-1.5 py-0.5 text-xs font-mono border transition-colors ${
                            d.maxTokens === tok
                              ? 'bg-primary text-primary-foreground border-primary font-medium'
                              : 'bg-muted/50 hover:bg-muted text-muted-foreground border-border'
                          }`}
                        >
                          {tok >= 1024 ? `${tok / 1024}k` : tok}
                        </button>
                      ))}
                    </div>
                  </div>
                </Field>

                <Field label="Top P" htmlFor="top-p">
                  <Input
                    id="top-p"
                    type="number"
                    step="0.05"
                    min="0"
                    max="1"
                    value={d.topP}
                    onChange={(e) => set('settings.setTopP', Number(e.target.value))}
                  />
                </Field>

                <Field label="Timeout (sec)" htmlFor="timeout">
                  <Input
                    id="timeout"
                    type="number"
                    min="15"
                    max="600"
                    value={d.timeoutSeconds}
                    onChange={(e) => set('settings.setTimeout', Number(e.target.value))}
                  />
                </Field>
              </div>

              <Field label="Examiner system prompt (optional override)" htmlFor="sys-prompt">
                <Textarea
                  id="sys-prompt"
                  rows={3}
                  className="align-top font-sans text-xs sm:text-sm"
                  placeholder="Leave empty to use default built in IELTS examiner prompt."
                  value={d.systemPrompt}
                  onChange={(e) => set('settings.setSystemPrompt', e.target.value)}
                />
              </Field>

              <div className="rounded-lg border bg-muted/30 p-4 space-y-3">
                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <div className="text-sm font-medium">Stream replies</div>
                    <div className="text-xs text-muted-foreground">Receive real time token streams during evaluation.</div>
                  </div>
                  <Switch checked={d.useStreaming} onCheckedChange={(v) => set('settings.setUseStreaming', v)} />
                </div>

                <Separator />

                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <div className="text-sm font-medium">Vision support</div>
                    <div className="text-xs text-muted-foreground">Send task images and charts to models that support multimodal input.</div>
                  </div>
                  <Switch checked={d.visionEnabled} onCheckedChange={(v) => set('settings.setVision', v)} />
                </div>

                <Separator />

                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <div className="text-sm font-medium">Auto-load offline models</div>
                    <div className="text-xs text-muted-foreground">{d.modelModeSummary}</div>
                  </div>
                  <Switch checked={d.modelAutoLoad} onCheckedChange={(v) => set('settings.setModelAutoLoad', v)} />
                </div>

                <Separator />

                <div className="flex items-center justify-between">
                  <div className="space-y-0.5">
                    <div className="text-sm font-medium">Speaking: submit and score after recording</div>
                    <div className="text-xs text-muted-foreground">
                      On (test mode): record once, the answer is submitted and scored straight away and the
                      transcript cannot be edited. Off keeps the transcript box for practice.
                    </div>
                  </div>
                  <Switch checked={d.speakingAutoSubmit} onCheckedChange={(v) => set('settings.setSpeakingAutoSubmit', v)} />
                </div>
              </div>

              {d.problems.length > 0 && (
                <div className="rounded-md border border-warning/30 bg-warning/10 p-3">
                  <div className="flex items-center gap-2 text-xs font-semibold text-warning mb-1">
                    <AlertCircle className="h-4 w-4" /> Configuration Notes
                  </div>
                  <ul className="list-disc pl-5 text-xs space-y-0.5 text-warning">
                    {d.problems.map((p, i) => (
                      <li key={i} className={p.isError ? 'text-destructive font-medium' : ''}>
                        {p.text}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              <div className="flex flex-wrap items-center justify-between gap-3 pt-2">
                <div className="flex flex-wrap gap-2">
                  <Button onClick={() => run('settings.save')}>Save settings</Button>
                  <Button
                    variant="outline"
                    disabled={!d.isValid || page.busy}
                    onClick={() => run('settings.testConnection')}
                  >
                    {page.busy ? 'Testing connection...' : 'Test connection'}
                  </Button>
                  <Button variant="outline" onClick={() => run('settings.compactDatabase')}>
                    Compact database
                  </Button>
                </div>

                <Button
                  variant="ghost"
                  className="text-muted-foreground hover:text-destructive"
                  onClick={() =>
                    setConfirm({
                      title: 'Reset settings',
                      body: 'Reset every setting on this page to its default values?',
                      confirmLabel: 'Reset',
                      onConfirm: async () => {
                        setConfirm(null)
                        await run('settings.reset')
                      },
                    })
                  }
                >
                  <RotateCcw className="h-4 w-4 mr-1" /> Reset to defaults
                </Button>
              </div>

              <p className="text-xs text-muted-foreground">
                Testing connection validates endpoint accessibility with the active inputs without saving to disk.
              </p>
            </CardContent>
          </Card>

          {d.hasDebugDetails && (
            <Card>
              <CardHeader>
                <CardTitle className="text-sm font-semibold">Connection Test Results</CardTitle>
              </CardHeader>
              <CardContent>
                <pre className="rounded bg-muted p-3 font-mono text-xs whitespace-pre-wrap leading-relaxed text-foreground">
                  {d.debugDetails}
                </pre>
              </CardContent>
            </Card>
          )}
        </TabsContent>

        {/* Tab 2: Sound & Microphone */}
        <TabsContent value="audio" className="mt-4 flex flex-col gap-5">
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between">
                <div>
                  <CardTitle className="text-base font-semibold">Audio Devices & Diagnostics</CardTitle>
                  <CardDescription>
                    Select audio outputs for listening recordings and input microphones for speaking tests.
                  </CardDescription>
                </div>
                <Button variant="outline" size="sm" onClick={() => refreshDevices(true)} className="gap-1.5">
                  <RefreshCw className="h-3.5 w-3.5" /> Refresh devices
                </Button>
              </div>
            </CardHeader>
            <CardContent className="flex flex-col gap-6">
              <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                <div className="flex flex-col gap-3 rounded-lg border p-4">
                  <div className="flex items-center gap-2 font-medium text-sm">
                    <Volume2 className="h-4 w-4 text-primary" />
                    <span>Speaker / Headphone</span>
                  </div>
                  <Field label="Selected output device">
                    <Select
                      value={d.audioOutputDeviceId || '__default'}
                      onValueChange={(v) => set('settings.setAudioOutput', v === '__default' ? '' : v)}
                    >
                      <SelectTrigger><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem value="__default">System default speaker</SelectItem>
                        {devices.speakers.map((s) => (
                          <SelectItem key={s.id} value={s.id}>{s.label}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </Field>
                  <Button
                    variant="outline"
                    disabled={!!audioBusy}
                    onClick={async () => {
                      setAudioBusy('speaker')
                      setAudioNote('Playing test chime on chosen speaker...')
                      try {
                        await playTestTone(d.audioOutputDeviceId)
                        setAudioNote('Test tone played. If you heard two clear beeps, audio playback is working correctly.')
                      } catch {
                        setAudioNote('Playback failed on this speaker. Please check the output device.')
                      } finally {
                        setAudioBusy('')
                      }
                    }}
                    className="w-full mt-1"
                  >
                    {audioBusy === 'speaker' ? 'Playing test tone...' : 'Play test sound'}
                  </Button>
                </div>

                <div className="flex flex-col gap-3 rounded-lg border p-4">
                  <div className="flex items-center gap-2 font-medium text-sm">
                    <Mic className="h-4 w-4 text-primary" />
                    <span>Microphone</span>
                  </div>
                  <Field label="Selected input microphone">
                    <Select
                      value={d.audioInputDeviceId || '__default'}
                      onValueChange={(v) => set('settings.setAudioInput', v === '__default' ? '' : v)}
                    >
                      <SelectTrigger><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem value="__default">System default microphone</SelectItem>
                        {devices.mics.map((m) => (
                          <SelectItem key={m.id} value={m.id}>{m.label}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </Field>
                  <Button
                    variant="outline"
                    disabled={!!audioBusy || !mediaSupported()}
                    onClick={async () => {
                      setAudioBusy('mic')
                      setAudioNote('Recording 3 seconds of audio. Please speak clearly...')
                      try {
                        const blob = await recordAndPlayback(d.audioInputDeviceId, 3)
                        setAudioNote(
                          blob
                            ? 'Recorded audio has played back. If you heard your voice, the microphone is ready.'
                            : 'Could not capture recording. Verify microphone access permissions.'
                        )
                        refreshDevices(false)
                      } catch {
                        setAudioNote('Microphone capture failed. Verify microphone device permissions.')
                      } finally {
                        setAudioBusy('')
                      }
                    }}
                    className="w-full mt-1"
                  >
                    {audioBusy === 'mic' ? 'Recording 3 seconds...' : 'Test microphone (3s)'}
                  </Button>
                </div>
              </div>

              {audioNote && (
                <div className="rounded-md border border-border bg-muted/40 p-3 text-sm leading-relaxed">
                  {audioNote}
                </div>
              )}

              {!mediaSupported() && (
                <div className="rounded-md border border-warning/30 bg-warning/10 p-3 text-xs text-warning">
                  Media device recording is not supported in this runtime environment.
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        {/* Tab 3: Appearance & Exam Preferences */}
        <TabsContent value="appearance" className="mt-4 flex flex-col gap-5">
          <Card>
            <CardHeader>
              <CardTitle className="text-base font-semibold">User Interface & Exam Environment</CardTitle>
              <CardDescription>
                Preferences take effect immediately and persist automatically across sessions.
              </CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-6">
              <div className="flex flex-col gap-2">
                <div className="text-sm font-semibold">Colour theme</div>
                <div className="flex items-center justify-between rounded-lg border p-4">
                  <div>
                    <div className="font-medium text-sm">{d.theme} theme</div>
                    <div className="text-xs text-muted-foreground">{d.themeSummary}</div>
                  </div>
                  <ThemeToggle value={d.theme} onChanged={(t) => run('settings.setTheme', { value: t })} />
                </div>
              </div>

              <Separator />

              <div className="flex flex-col gap-2">
                <div className="text-sm font-semibold">Exam text scaling</div>
                <p className="text-xs text-muted-foreground">{d.textSizeSummary}</p>
                <div className="flex flex-wrap gap-2 pt-1">
                  {d.textSizeOptions.map((t) => (
                    <Button
                      key={t}
                      size="sm"
                      variant={d.selectedTextSize === t ? 'default' : 'outline'}
                      onClick={() => set('settings.setTextSize', t)}
                      className="min-w-24"
                    >
                      {t}
                    </Button>
                  ))}
                </div>
              </div>

              <Separator />

              <div className="flex items-center justify-between rounded-lg border p-4">
                <div className="space-y-0.5">
                  <div className="text-sm font-medium">Automatic fullscreen on exam start</div>
                  <div className="text-xs text-muted-foreground">{d.fullscreenSummary}</div>
                </div>
                <Switch
                  checked={d.fullscreenOnStart}
                  onCheckedChange={(v) => set('settings.setFullscreenOnStart', v)}
                />
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        {/* Tab 4: Offline Models */}
        <TabsContent value="offline" className="mt-4 flex flex-col gap-5">
          <Card>
            <CardHeader>
              <CardTitle className="text-base font-semibold">Embedded ONNX Models</CardTitle>
              <CardDescription>
                Local offline inference running on CPU. Missing models gracefully degrade to local scoring or typed answers.
              </CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              {!models ? (
                <div className="py-6 text-center text-sm text-muted-foreground">Loading model catalog...</div>
              ) : (
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  {models.map((m) => (
                    <div
                      key={m.name}
                      className="flex flex-col justify-between rounded-lg border p-4 transition-colors hover:border-primary/40 bg-card"
                    >
                      <div>
                        <div className="flex items-center justify-between gap-2 mb-2">
                          <span className="font-semibold text-sm tracking-tight text-foreground truncate">
                            {m.name}
                          </span>
                          <Badge
                            variant={m.ready ? 'secondary' : 'outline'}
                            className={
                              m.ready
                                ? 'bg-success/10 text-success border-success/30'
                                : 'border-warning/50 text-warning'
                            }
                          >
                            {m.ready ? 'Ready' : 'Missing'}
                          </Badge>
                        </div>
                        <Badge variant="outline" className="text-xs mb-2">
                          {m.skill}
                        </Badge>
                        <p className="text-xs text-muted-foreground leading-relaxed">
                          {m.purpose}
                        </p>
                      </div>

                      <div className="mt-4 pt-2 border-t text-xs text-muted-foreground flex flex-col gap-1">
                        <div><span className="font-medium text-foreground">License:</span> {m.license}</div>
                        <div className="truncate"><span className="font-medium text-foreground">Source:</span> {m.source}</div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        {/* Tab: Diagnostics. One screen that says whether the session is healthy
            and shows the log, so a problem can be read without a debugger. */}
        <TabsContent value="diagnostics" className="mt-4 flex flex-col gap-5">
          <DiagnosticsPanel onError={(m) => page.setError(m)} />
        </TabsContent>

        {/* Tab 5: About & Updates */}
        <TabsContent value="about" className="mt-4 flex flex-col gap-5">
          <Card>
            <CardHeader>
              <CardTitle className="text-base font-semibold">Software Updates</CardTitle>
              <CardDescription>{d.appVersionLabel}</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              <div className="flex items-center justify-between rounded-lg border p-4">
                <div className="space-y-0.5">
                  <div className="text-sm font-medium">Automatic update checks</div>
                  <div className="text-xs text-muted-foreground">Check for newer releases when opening the app.</div>
                </div>
                <Switch
                  checked={d.updateCheckOnStartup}
                  onCheckedChange={(v) => set('settings.setUpdateCheckOnStartup', v)}
                />
              </div>

              <div className="flex flex-wrap items-center gap-3">
                <Button variant="outline" disabled={page.busy} onClick={() => run('settings.checkUpdate')}>
                  Check for updates
                </Button>
                {d.updateAvailable && (
                  <Button variant="default" disabled={page.busy} onClick={() => run('settings.downloadUpdate')}>
                    Download update
                  </Button>
                )}
                {d.updateDownloaded && (
                  <Button onClick={() => run('settings.applyUpdate')}>
                    Open release package
                  </Button>
                )}
              </div>

              {d.updateStatus && (
                <div className="rounded border bg-muted/40 p-3 text-xs leading-relaxed text-foreground">
                  {d.updateStatus}
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base font-semibold">About IELTop</CardTitle>
              <CardDescription>{d.aboutLine}</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-3">
              {/* The wordmark sits here, where the name is first introduced, so
                  the symbol beside it in the sidebar links to the full name. */}
              <img
                src="./assets/IELTop-motion-wordmark.svg"
                alt="IELTop"
                className="h-8 w-auto self-start"
              />
              <div className="grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
                <div className="rounded border p-3">
                  <div className="font-medium text-foreground">{d.authorLine}</div>
                  <div className="text-muted-foreground mt-0.5">{d.licenseLine}</div>
                </div>
                <div className="rounded border p-3">
                  <div className="font-medium text-foreground">Storage Location</div>
                  <div className="text-muted-foreground mt-0.5 truncate">{d.dataFolder}</div>
                </div>
              </div>

              <div className="flex flex-wrap gap-2 pt-2">
                <Button variant="outline" size="sm" onClick={() => run('settings.openDataFolder')} className="gap-1.5">
                  <FolderOpen className="h-4 w-4" /> Open data folder
                </Button>
                <Button variant="outline" size="sm" onClick={() => run('settings.openProjectPage')} className="gap-1.5">
                  <ExternalLink className="h-4 w-4" /> Open project page
                </Button>
              </div>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>

      {d.statusMessage && (
        <div className="rounded-md border border-border bg-card px-4 py-2.5 text-xs text-muted-foreground">
          {d.statusMessage}
        </div>
      )}

      <Confirm
        open={!!confirm}
        title={confirm?.title ?? ''}
        body={confirm?.body ?? ''}
        confirmLabel={confirm?.confirmLabel ?? 'Confirm'}
        onCancel={() => setConfirm(null)}
        onConfirm={() => confirm?.onConfirm?.()}
      />
    </div>
  )
}

