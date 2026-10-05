# AGENTS.md - Luật bắt buộc cho IELTop

App `.NET 10` học IELTS offline, opensource.
Stack: .NET MAUI / WinUI 3 (cửa sổ native) + React/Vite (web UI) + ONNX Runtime + SQLite (sqlite-net-pcl). Một core dùng chung `IELTop.Core` cho cả logic lẫn model.
Desktop client chỉ hỗ trợ Windows. Cửa sổ chính chạy trong `BlazorWebView`: nó tải app React đã build qua static web assets, và cầu đi qua JS interop (`JsBridge` + `[JSInvokable]`). Cửa sổ thi chạy trong WebView2 thường, cầu đi qua `StaticFileServer` trên loopback (`/api/*` + SSE) vì trang đó không có Blazor runtime. Hai kênh dùng chung một `BridgeRouter`.
Kênh postMessage của WebView2 chỉ thuộc cửa sổ thi. Trên cửa sổ chính, `window.chrome.webview` vẫn tồn tại nhưng là kênh nội bộ của Blazor, nên `bridge.ts` chỉ coi postMessage là kênh hợp lệ khi trang chạy trên `http:` (loopback), không phải `https://0.0.0.1` (virtual host của Blazor).
Mọi agent và mọi commit đều phải tuân thủ file này.

## 1. Luật thiết kế UI/UX

Mục này là các nguyên tắc chung. Khi thêm màn hình hay component mới, theo nguyên tắc, không cần thêm luật mới cho từng trường hợp.

1.1. Ngôn ngữ và ký tự.
- UI toàn bộ tiếng Anh; comment code và tài liệu nội bộ có thể tiếng Việt.
- Cấm em dash (U+2014), en dash (U+2013), và emoji trang trí trong UI.
- Icon lấy từ `lucide-react` qua component; cấm glyph chữ ("x", "◀", "▶") làm nhãn và cấm tự vẽ path tay.

1.2. Component phải thật.
- Mọi control render ra phải chạy thật hoặc bị `disabled` kèm lý do rõ ràng.
- Cấm nút chết, nút "Coming soon", nút demo. Luồng chính phải đi hết: bấm, phản hồi, kết quả hoặc lỗi dễ hiểu.
- Nút mà bấm vào chắc chắn lỗi đã biết trước thì disable kèm lý do, không để bấm rồi mới báo.

1.3. Ngôn ngữ hiển thị.
- UI nói tiếng người dùng. Cấm từ kỹ thuật thô: "InferenceSession", "Tensor", "Encoder/Decoder", "DbContext", "NullReference", "StackTrace".
- Lỗi ghi log đầy đủ; UI chỉ hiện câu ngắn: nguyên nhân, cách sửa.
- Đúng: "The model could not be opened. Check the file in Content/Assets/Models."
- Sai: "InferenceSession failed: tensor dim mismatch".

1.4. UX dùng được ngay.
- Một màn một việc; mỗi mục sidebar mở ra một màn hình thật; sidebar tối đa 7 mục. Exam Editor là một tab trong Paper Library, không phải mục riêng.
- Thao tác quá 1 giây có trạng thái chờ (disable nút, "Working...").
- Ô nhập luôn có placeholder; nhiều dòng dùng `<textarea>` căn từ trên.
- Cỡ chữ tối thiểu 12px ở mọi text UI; tương phản đủ đọc.
- Hỗ trợ bàn phím cơ bản: Tab đúng thứ tự, Enter để xác nhận.

1.5. Style dùng chung, một hệ duy nhất.
- Style bằng Tailwind CSS utility cho bố cục và khoảng cách. Token màu, chữ nằm trong `src/styles.css` theo quy ước shadcn/ui (`--background`, `--foreground`, `--card`, `--primary`, `--border`, `--ring`), có cả bảng màu sáng và tối. Sửa token là đổi toàn cục.
- Khung thi có bộ token riêng `--exam-header`, `--exam-bar`, `--exam-surface`, `--exam-block`, `--exam-instruction` (kèm biến `@theme` `bg-exam-*`), tách khỏi token app để không đổi màn chính. Cửa sổ thi đọc theo chuẩn IDP: header tối, thân xám, khối hướng dẫn viền màu, khối câu hỏi trắng, dải đáy liệt kê part và số câu. Cấm dùng lại `--card`/`--muted` cho khung thi.
- Component lấy từ shadcn/ui, đặt ở `UserInterface/src/components/ui/`; cài thêm bằng `npx shadcn@latest add <name>`, không tự viết lại. Component ghép riêng của app nằm ở `UserInterface/src/components/` (ví dụ `shared.jsx`, `Field.jsx`, `ThemeToggle.jsx`).
- Theme sáng, tối, hệ thống: mặc định `System`, lưu ở `AppSettings.UiTheme`, áp bằng class `.dark` trên `<html>` qua `src/lib/theme.js`. Cấm đọc `prefers-color-scheme` trực tiếp trong component.
- Cấm trộn thêm framework UI khác (Mantine, Chakra, MUI...). Icon vẫn chỉ `lucide-react`.
- Biểu đồ vẽ bằng SVG hoặc CSS đơn giản, không thêm thư viện chart.
- Muốn đổi hay thêm hệ style phải sửa mục này trước, kèm lý do.

1.6. Offline-first, LLM là tùy chọn.
- App phải chạy đầy đủ khi không có model ONNX, không có mạng, không có API key.
- Thiếu file model thì hiện hướng dẫn trong `Content/Assets/Models/README.md`, không crash, không cửa sổ trắng.
- Tính năng AI disable kèm câu giải thích khi chưa cấu hình; không có LLM thì app vẫn dùng được.
- Không thêm thư viện nặng làm chậm máy yếu; import/export dùng text, cấm auto-thêm OCR hay GPU.

1.7. Gọi AI qua API chuẩn OpenAI.
- Mọi lời gọi mô hình ngôn ngữ đi qua `ILlmService`; UI cấm tự dựng `HttpClient`.
- Hỗ trợ mọi server chuẩn OpenAI chat completions: OpenAI, gateway tương thích Azure, Ollama, LM Studio, llama.cpp server, vLLM.
- Base URL, tên model, API key, temperature, max tokens để trong Settings, lưu ở `%LocalAppData%/IELTop/settings.json`; API key mã hóa (AES-GCM, key riêng trên máy), cấm ghi plain text ra file hay log.
- Mọi lời gọi AI async, có `CancellationToken`, có xử lý lỗi mạng và timeout ra câu tiếng Anh ngắn.
- Settings đổi thì nút AI tự bật/tắt ngay; cấm để nút AI bật mà gọi thất bại. LLM đã cấu hình nhưng server không chạy thì tự rơi về đường offline.
- Trước khi gọi AI phải qua `LlmConfigValidator` để báo lỗi định dạng ngay trên UI.
- Vision là tùy chọn riêng, mặc định tắt. Chỉ gửi ảnh khi user bật `LlmVisionEnabled`; model không có vision thì không gửi ảnh.
- Điểm band AI trả về chỉ là ước lượng luyện tập, không phải điểm IELTS chính thức.

1.8. Nguồn dữ liệu chuẩn, không bịa.
- Nội dung học (đề, đáp án, phiên âm, ví dụ) lấy từ nguồn mở có giấy phép dùng lại, ghi rõ nguồn.
- Không bịa số liệu, điểm ước lượng, hay kết quả AI khi chưa chạy model.
- Điểm band trong Mock Test chỉ là ước lượng, phải ghi rõ không phải điểm IELTS chính thức.

## 2. Luật code

2.1. Cấu trúc thư mục, đặt đâu làm đó.
- Repo có 3 phần: `IELTop.Core` (dùng chung, đa nền tảng), `IELTop.Desktop` (client .NET MAUI/WebView2 + React, Windows), `Content` (nội dung đọc dùng chung), cộng thêm `IELTop.Tests` (xUnit cho logic Core).
- Logic có thể test không cần cửa sổ (policy, tracker, engine với fake port) phải có test trong `IELTop.Tests`.
- `IELTop.Core/Models/` — entity thuần, không gọi DB, không gọi ONNX, không phụ thuộc nền tảng.
- `IELTop.Core/Data/` — chỉ `AppDbContext` và migration/seed.
- `IELTop.Core/Services/Ai/` — mọi code ONNX và code gọi LLM nằm đây, UI cấm `new InferenceSession` và cấm `HttpClient` trực tiếp.
- `IELTop.Core/Services/Exam/` — engine thi (setup, chấm điểm, review), không phụ thuộc UI.
- `IELTop.Core/Services/Exam/` - mọi hành vi ngoài app (fullscreen, focus, always on top) phải đi qua một port trong Core (`IExamSessionController`), không gọi API nền tảng. Host cài đặt port (`IELTop.Desktop/Web/MauiExamSession.cs`). Mặc định là `NullExamSessionController`. Luật strict mode gom vào `StrictModePolicy`, debounce focus vào `StrictFocusTracker`, đều là lớp thuần, test được. Cửa sổ thi chỉ giữ lại việc đóng khi một bài đang chạy (`CloseGuard` trả về phase thật); nộp hoặc hủy xong thì đóng thẳng. `FullscreenOnStart` của cửa sổ chính áp dụng ở `IELTop.Desktop/Web/MainWindowPreferences.cs`, gọi từ `App.CreateWindow`.
- `IELTop.Core/Services/App/` — service cho từng màn hình (Library, Editor, Results, Servers, Settings), trả snapshot thuần dữ liệu.
- `IELTop.Core/Services/Learn/` — màn Study: `LessonService` đọc JSON bài giảng trong `Content/Assets/Lessons/` và tìm kiếm để trích nguồn; `StudyService` giữ phiên học (chat, luyện tập) trong SQLite, sinh đề và chấm qua `ILlmService`. Chat và luyện tập cần model; đọc bài giảng và từ vựng chạy offline. Prompt nằm ở `IELTop.Core/Services/Ai/StudyPrompts.cs`. Bài giảng chuẩn hóa bằng `tools/lesson-ingest/`, mỗi file một unit, giữ nguyên chữ gốc, có `source`. Dạng câu hỏi luyện tập: `gap`, `completion`, `single`, `multiple`, `short`, `match` (match lưu ở `MatchRowsJson` dạng `{label,answer}`, trả lời `label=value;label=value`); parser đọc từng câu độc lập và **thử lại 1 lần** khi model trả JSON hỏng. Chat có công cụ (`study.chat.runTool`): lookup, translate, summarize, flashcards, fix, paraphrase.
- `IELTop.Core/Services/Audio/`, `IELTop.Core/Services/Storage/` — theo tính năng. Core tối đa `net10.0`, cấm `net10.0-windows`, cấm `System.Windows`, `NAudio`, `System.Speech`, DPAPI trong Core.
- `IELTop.Desktop/MauiProgram.cs` — host .NET MAUI. `IELTop.Desktop/Bridge/` — router JSON giữa web UI và C#. `IELTop.Desktop/UserInterface/` — React + Vite. `IELTop.Desktop/wwwroot/` — UI đã build, không commit.
- Cửa sổ chính dùng `BlazorWebView` (HostPage `wwwroot/index.html`), cầu Blazor nằm ở `IELTop.Desktop/Bridge/JsBridge.cs` và `IELTop.Desktop/Web/BlazorHost.razor`. Cửa sổ thi dùng WebView2 thường + cầu HTTP. Trang React gọi `call()` trong `bridge.ts`, tự chọn kênh: Blazor trước, HTTP sau.
- Logic dùng chung phải nằm ở `IELTop.Core`, web UI chỉ gọi qua bridge, không tự tính điểm hay đọc DB.
- `tools/` — công cụ Python (conda `.venv` trong từng thư mục), tách khỏi app C#.
- `Content/Assets/Models/` — model ONNX. `Content/Assets/Exams/` — nội dung JSON. `Content/Assets/Lessons/` — bài giảng đã chuẩn hóa, mỗi unit một JSON. `Content/Assets/Audio/` — file nghe. `Content/Assets/Images/` — ảnh. `Content/servers.txt` — danh sách server cộng đồng.
- Muốn thêm model ONNX mới: chỉ thêm 1 dòng `OnnxModelSlot` trong `OnnxModelRegistry.cs`.
- Muốn thêm nội dung học mới: thêm file JSON, không sửa code.
- Muốn thêm màn hình web mới: thêm 1 file trong `UserInterface/src/pages/`, đăng ký method trong `Bridge/`.

2.2. Bridge và DI.
- Bridge nhận method qua `BridgeRouter`, trả `{ id, result }` hoặc `{ id, error }`. Mọi text trả về UI là tiếng Anh.
- Service nhận phụ thuộc qua constructor, đăng ký một lần trong `Program.cs`.
- Không dùng `ServiceLocator`, không `new OnnxService()` rải rác.
- Không để state toàn cục tĩnh, trừ `OnnxModelRegistry`.
- `IOnnxService`, `IContentServerClient` và mọi service có tài nguyên phải `Dispose` đúng cách.
- Mỗi mục sidebar phải có service riêng; mỗi màn hình mở ra một trang thật.

2.3. Async, DB, file.
- I/O luôn async: `async Task`, truyền `CancellationToken` khi infer hoặc gọi LLM.
- Infer và gọi LLM không được chạy trên UI thread; bridge handler async, host chạy `Task.Run`.
- SQLite (sqlite-net-pcl): gọi `AppDbContext.EnsureCreated()`, query ngắn gọn, context dùng xong dispose.
- Đường dẫn file dùng `Path.Combine`, không nối chuỗi tay. DB lưu ở `%LocalAppData%/IELTop`, model ở `Content/Assets/Models`, nội dung người dùng ở `%LocalAppData%/IELTop/content`.
- File `.onnx`, `.db`, `.sqlite`, `settings.json` không commit. Chỉ giữ `README.md`, `.gitkeep`, `phoneme-map.json`.
- Thêm thư mục content mới thì phải thêm `CopyToOutputDirectory` trong `IELTop.Desktop.csproj`.
- Preference áp dụng ngay (cỡ chữ exam, full screen) ghi xuống `settings.json` ngay khi đổi, không chờ bấm Save.
- Nút "Test connection" chỉ test cấu hình đang hiện trên màn hình, cấm ghi settings xuống đĩa.

2.4. Đặt tên và ngôn ngữ.
- Code, namespace, commit bằng tiếng Anh.
- Tên kiểu dữ liệu, service rõ nghĩa; interface bắt đầu bằng `I`, async kết thúc bằng `Async`.
- Không viết tắt khó hiểu. Không comment sáo rỗng. Comment chỉ giải thích "vì sao", không nhắc lại "làm gì".

2.4b. Cấm code chết.
- Route bridge không được UI gọi thì phải xoá; method UI gọi mà chưa đăng ký phải bổ sung.
- Property, state trong service mà không màn nào đọc thì phải xoá.
- Trước khi báo xong, so khớp danh sách `router.Register(...)` với lời gọi `call('...')` trong `UserInterface/src`.

2.5. Xử lý lỗi.
- Không `catch {}` nuốt lỗi. Không hiện exception thô cho người dùng.
- Service trả về `bool + out error` hoặc kết quả rõ ràng, ViewModel chuyển thành câu tiếng Anh ngắn gọn.
- Mọi lỗi load model phải nói: tên slot, tên file thiếu, đường dẫn đầy đủ.

2.6. Bảo mật thư viện.
- Không để package có lỗ hổng. Chạy `dotnet list package --vulnerable --include-transitive` trước khi báo xong.
- Có cảnh báo NU1902/NU1903 thì nâng package trực tiếp lên bản đã vá, không chờ bản transitive.
- Không khoá cứng package cũ vì lười nâng; ghi lại lý do nếu buộc phải giữ.

## 3. Luật ONNX

- Chỉ load model qua `IOnnxService.TryLoad(name, out error)`.
- Mỗi session dùng `SessionOptions` riêng, `GraphOptimizationLevel.ORT_ENABLE_ALL`, ưu tiên CPU.
- Input audio chuẩn Whisper: WAV 16kHz, mono, 16-bit.
- Không hardcode đường dẫn tuyệt đối tới model.
- Chưa có model thật thì code vẫn phải build và chạy demo được.
- Chấm phát âm dùng model wav2vec2 CTC cấp âm vị, không dùng Whisper gốc để bắt lỗi sai.
- Chỉ dùng bản Base INT8 cho client để chạy mượt trên CPU.
- Model âm vị phát ra IPA. `phoneme-map.json` phải dùng cùng hệ IPA, cấm dùng ARPABET lẫn lộn.
  Sau khi đổi model phải chạy `tools/speaking-mdd/check_phoneme_map.py` để xác nhận khớp.
- onnxruntime không lượng tử hóa được Conv của wav2vec2, chỉ lượng tử hóa `MatMul`/`Gemm`.
- Mọi `OnnxModelSlot` phải trỏ tới model có thật, tải được, và ghi rõ `Source` + `License`.
  Cấm thêm slot "để dành" hay model không tồn tại. Model non-commercial (như CC-BY-NC-SA)
  phải ghi rõ trong License và trong `Content/Assets/Models/README.md`.
- UI phải show `Source` và `License` của từng slot (mục Offline models trong Settings),
  không cất trong code. Model non-commercial phải hiện rõ trên UI.
- Mỗi service dùng model phải có `const string SlotName` trỏ tới một slot có thật trong
  `OnnxModelRegistry`. `OnnxModelRegistry.IsComplete` phải kiểm **đủ mọi file** slot cần
  (model + file phụ + vocab/spiece/config), không chỉ 1 file. Thêm model thì thêm cả dòng
  copy trong `IELTop.Desktop.csproj`; build ra phải thấy file trong `bin/.../Assets/Models`.
  `--selftest` in "N slot(s), M installed" và fail nếu thiếu slot mà service cần.
- Server ONNX cho chương trình ngoài: `IELTop.Desktop.exe --model-server` mở
  `OnnxHttpServer` (`IELTop.Core/Services/Protocol/`) trên loopback, chỉ HTTP, có bearer
  token ghi ở `%LocalAppData%\IELTop\model-server.json`. Route: `/health`, `/v1/stt`,
  `/v1/tts`, `/v1/pronunciation/check`, `/v1/grammar/check`. Audio truyền base64, không
  truyền đường dẫn. Tài liệu ở `tools/onnx-server/README.md`.
- TTS (Piper) cần `espeak-ng.exe` **và `libespeak-ng.dll`** trong `espeak-ng/`; thiếu DLL thì
  espeak thoát mã `0xC0000135` và voice ghi wav rỗng. Output VITS có thể 4 chiều
  `[1,1,1,N]`: đọc độ dài ở **chiều cuối**, không phải chiều thứ 3.
- Model **không tự load khi mở app**. Chế độ thường: load khi bước cần (on demand).
  Bật "Keep ready" (`AppSettings.ModelAutoLoad`): `ModelLoadCoordinator.PrepareAsync` preload
  đúng slot của kỳ thi lúc bắt đầu chấm/ghi âm rồi `ReleaseAfterUse` giải phóng sau. `WritingSlots`
  = `gec-t5-small`, `SpeakingSlots` = whisper + wav2vec2. `/health` của model-server cho biết
  slot nào đang `loaded`.
- Kiểm model thật (không chỉ file tồn tại): `$env:IELTOP_ONNX_LIVE=1; dotnet test --filter
  FullyQualifiedName~OnnxModelsLiveTests`. Test dùng TTS đọc câu, rồi STT nghe lại, wav2vec2
  chấm (đúng phải cao hơn sai), GEC sửa câu sai. Model đọc từ `Content/Assets/Models`, không
  copy vào output test.
- Chấm phát âm dùng **GOP** (goodness of pronunciation): mỗi âm nghe được mang khoảng cách
  log-posterior trung bình của âm đích trên các frame của nó, về 0..1 (`PhonemeEdit.Gop`,
  trung bình từ là `WordPronunciation.Gop`, cả bài là `MddResult.MeanGop`). Âm khớp nhưng yếu
  vẫn bị gắn cờ. Đừng đoán GOP bằng mắt: kiểm bằng `--model-server` + `/v1/pronunciation/check`.
- **Fluency đo từ audio** (`Services/Audio/SpeechTiming.cs`): VAD năng lượng 20ms, hangover
  100ms, pause từ 250ms. Lưu vào part (`MeasuredSpeechSeconds`, `PauseCount`, `MeanPauseSeconds`),
  hiện qua `SpeechTimingLabel`, đưa vào prompt chấm và trừ điểm fluency khi ngắt nhiều/dài
  (`ApplyPaceCap`). Ngưỡng là heuristic luyện tập, không phải chuẩn IELTS.
- GPU tự dò: Core giữ provider CPU (`IOnnxExecutionProvider`, `CpuOnnxExecutionProvider`).
  Host Windows cài `Microsoft.ML.OnnxRuntime.DirectML` và cài port bằng
  `DirectMlExecutionProvider` (dò `Win32_VideoController`, bỏ adapter ảo/Basic Render). Có GPU
  thì chạy DirectML (NVIDIA/AMD/Intel qua DX12), không có thì fallback CPU. `/health` của
  model-server in `provider`. Không hardcode vendor; Core cấm phụ thuộc Windows.
- STT có 2 slot `stt-whisper-{small,base}-en` (tiny đã bỏ vì quá yếu).
  `OnnxModelRegistry.ResolveSttSlot()` chọn bản **lớn nhất có sẵn**; thêm file vào
  `Content/Assets/Models` là app tự dùng, không sửa code.
- Speaking mặc định **test mode**: test mic trước, ghi **một lần**, dừng là nộp và chấm luôn;
  transcript lấy từ STT và **không sửa được** (SetTranscript bị từ chối). `AppSettings.SpeakingAutoSubmit`
  (mặc định true) tắt để giữ ô transcript cho luyện tập. `run.SpeakingAutoSubmit` báo cho UI.

## 3b. Luật dữ liệu lịch sử bài thi

- `ExamAttempt.Scope` lưu tập skill đã thi, tính từ các part thật: 4 skill thì "Full test",
  còn lại thì nối bằng " + ", ví dụ "Reading", "Listening + Reading".
- Filter ở Results so khớp theo chuỗi con (`Contains`), không so bằng tuyệt đối, để một
  filter skill đơn lẻ vẫn bắt được attempt nhiều skill.
- Cấm hardcode `Scope` thành "Full test" cho mọi lần nộp.

## 4. Luật Mock Test (kiểu IDP)

- Đề nằm trong `Content/Assets/Exams/*.json`, không hardcode nội dung trong code.
- Một đề gồm nhiều part, mỗi part có `skill`, `minutes`, `material`, `questions`.
- Giao diện thi phải giống thi máy thật: một màn hình, có đồng hồ đếm ngược, điều hướng part, một câu hỏi một khối.
- Thiết kế giao diện thi bo vuông chuẩn IDP/computer-delivered IELTS: toàn bộ khung hướng dẫn, hộp câu hỏi, ô nhập liệu và nút bấm để bo vuông (rounded-none hoặc rounded-xs), không dùng bo tròn mềm (rounded-lg/xl).
- Tận dụng bề ngang màn hình cân đối, không thu hẹp nội dung tạo khoảng trống lớn bất thường ở 2 bên mép màn hình khi thi.
- Đáp án lưu theo từng part, chấm toàn bộ đề khi submit, không chỉ part đang xem.
- Part Writing thu bài luận qua ô nhập, không auto chấm điểm; ghi rõ cần giáo viên hoặc AI chấm.
- Hết giờ tự chuyển part kế tiếp, hết part cuối thì tự submit.
- Sau khi submit có màn xem lại đáp án từng câu.
- Nguồn đề phải ghi trong trường `source`, chỉ dùng nội dung có quyền phân phối.

## 5. Luật nội dung luyện tập

- Nội dung mock test nằm trong `Content/Assets/Exams/*.json`, mỗi file gồm nhiều part theo skill.
- Audio Listening lấy từ `Content/Assets/Audio/`. Thiếu file audio thì UI báo rõ tên file thiếu, không crash, và hiện transcript thay vì giả giọng đọc.
- Audio do WebView phát; đường dẫn file được host đổi thành URL qua `/media/audio/`, không dùng `file://`.
- Mọi file JSON phải có trường `source` ghi nguồn và giấy phép.
- Chấm câu hỏi trắc nghiệm làm cục bộ, không cần model, để app chạy được hoàn toàn offline.

## 6. Luật nhập và xuất nội dung

- Nhập file hỗ trợ text: txt, md, json, csv, pdf, docx. Cấm auto-thêm OCR hay GPU.
- `FileTextExtractor` chỉ đọc text, không render ảnh. PDF scan thì báo rõ không hỗ trợ.
- Không có LLM thì nhập vẫn chạy: lưu text thô vào đúng phần, người dùng tự điền câu hỏi.
- Có LLM thì mới nháp câu hỏi. Nháp xong phải cho người dùng xem và sửa trước khi lưu.
- Nội dung người dùng nhập lưu ở `%LocalAppData%/IELTop/content`, không ghi vào thư mục cài đặt.
- Thư mục `Content` đi kèm app chỉ đọc, cấm ghi hay tạo thư mục trong đó lúc chạy.
- Xuất ra JSON hoặc Markdown, mỗi lần xuất tạo một thư mục mới, không ghi đè.
- Mọi lời gọi AI trong nhập/xuất đi qua `ILlmService`, có `CancellationToken`.

## 7. Quy trình làm việc của agent

1. Đọc `IELTop.Core.csproj`, `IELTop.Desktop.csproj`, `Program.cs`, file liên quan trước khi sửa.
2. Sửa ít nhất có thể. Ưu tiên sửa file có sẵn hơn tạo file mới.
3. Không tạo file `.md` mới trừ khi được yêu cầu.
4. Sau khi sửa code C# hoặc web UI: chạy `dotnet build IELTop.slnx --nologo -v minimal` ở gốc repo, fix tới khi 0 error. `dotnet build` tự dựng web UI trước.
5. Chạy app thử một lần để chắc không crash khi mở cửa sổ. Chạy `IELTop.Desktop.exe --selftest` trước: nó chạy toàn bộ kiểm tra (database, bài giảng, papers, vài route bridge) mà **không mở cửa sổ**, in một dòng mỗi mục, exit 0 khi pass, 2 khi fail. Chỉ mở cửa sổ khi cần kiểm chứng UI.
6. Muốn lái UI thật để kiểm chứng luồng, dùng `tools/debug-desktop/launch.cmd` (mở sẵn cổng debug, tự chạy de-elevated) và `tools/debug-desktop/cdp.ps1` để chạy JS / bấm nút / chụp ảnh qua CDP. Nền tảng: shell elevated bị WebView2 **bỏ qua** biến `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, nên phải chạy qua `explorer.exe`; cờ chỉ ăn khi app không elevated. URL trang có kèm `?k=<access key>`, key đó là bí mật nên không in ra log. Cách khác khi cần kiểm chứng chuyên sâu: `IELTop.Desktop.exe --selftest --live` gọi model thật theo `llm.txt`; hoặc `dotnet test --filter "FullyQualifiedName~StudyLiveLlmTests"` để kiểm LLM bám bài giảng (tự bỏ qua nếu không có `llm.txt`).
7. Đọc log thay vì đoán: `%LocalAppData%\IELTop\logs\latest.log` là phiên mới nhất, kèm mọi lời gọi bridge (thời gian, args tóm tắt không lộ bí mật), lỗi C# đầy đủ stack, và lỗi JS từ trang. Tab Diagnostics trong Settings xem nhanh: bộ đếm sức khỏe, call chậm nhất, tail log, mức log, marker. Đặt `IELTOP_LOG=trace|debug|info|warn|error` để đổi mức khi chạy (mức `Info` **không** ghi dòng từng call; muốn thấy thì để `Debug`/`Trace`). Mọi lời gọi bridge tự log qua `BridgeRouter`; muốn log thêm thì gọi `AppLog.*`, không `Console.WriteLine` rải rác. Log ghi **cả** file phiên và `latest.log`, đọc được lúc đang mở (dùng `FileShare.ReadWrite`).
8. Hướng dẫn debug đầy đủ nằm ở `tools/debug-desktop/README.md`: chạy không cửa sổ, đọc log, lái UI qua CDP, test tĩnh, kiểm LLM bằng `llm.txt`. Thêm tính năng mới thì phải thêm một dòng vào `--selftest` (hoặc một test) để lần sau bắt lỗi hồi quy mà không cần mở cửa sổ.
9. Không commit, không push, không đổi config git khi chưa được yêu cầu.
10. Trả lời ngắn gọn, tiếng Việt, liệt kê file đã đổi dạng `đường_dẫn:dòng`.

## 8. Checklist trước khi báo xong

- [ ] `dotnet build` 0 error.
- [ ] `IELTop.Desktop.exe --selftest` exit 0 (14/14 pass; thêm `--live` thì 16/16).
- [ ] Log không có dòng `ERROR` lạ; tab Diagnostics báo Healthy.
- [ ] Toàn bộ text UI là tiếng Anh.
- [ ] Không có em dash, en dash, hay emoji mới trong UI.
- [ ] Không có cỡ chữ nhỏ hơn 12px trong CSS.
- [ ] Ô nhập nhiều dòng dùng `<textarea>` căn chữ từ trên.
- [ ] Không có nút bấm chết; mỗi mục sidebar mở ra màn hình thật.
- [ ] Không có nhãn nút kiểu glyph ("x", "◀", "▶").
- [ ] Mọi route bridge khớp với lời gọi `call('...')` trong `UserInterface/src`, không thừa không thiếu.
- [ ] Mỗi `OnnxModelSlot` hiện `Source` và `License` trên màn Settings.
- [ ] Icon dùng `lucide-react`, không vẽ path tay.
- [ ] Chỉ một hệ style: Tailwind + token shadcn trong `styles.css`; component ở `components/ui/`, ghép riêng ở `components/`; không còn Mantine hay class ad-hoc thiếu định nghĩa.
- [ ] Theme sáng, tối, hệ thống hoạt động; mặc định `System`; lưu ở `AppSettings.UiTheme`.
- [ ] Không có thuật ngữ kỹ thuật thô hiện ra UI.
- [ ] App chạy được khi thiếu file `.onnx`, mất mạng, và không có API key.
- [ ] Mọi lời gọi AI đi qua `ILlmService`, không tự dựng `HttpClient` trong UI.
- [ ] API key không lưu plain text; nút AI disable khi chưa cấu hình.
- [ ] Không có LLM thì nhập file vẫn lưu được text, không bị chặn.
- [ ] Nội dung người dùng nhập nằm ở `%LocalAppData%/IELTop/content`, không ở thư mục cài đặt.
- [ ] Mock Test chấm đúng khi submit, có xem lại đáp án, và ghi rõ điểm chỉ là ước lượng.
- [ ] `dotnet list package --vulnerable --include-transitive` và `npm audit` không còn package lỗi.
- [ ] File lớn (model, node_modules, wwwroot) không lọt vào git.

