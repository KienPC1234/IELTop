# AGENTS.md - Luật bắt buộc cho IELTop

App `.NET 10` học IELTS offline, opensource, đa nền tảng.
Stack: Photino.NET (cửa sổ native) + React/Vite (web UI) + ONNX Runtime + SQLite EF Core. Một core dùng chung `IELTop.Core` cho cả logic lẫn model.
Mọi agent và mọi commit đều phải tuân thủ file này.

## 1. Luật thiết kế UI/UX

1.1. Giao diện toàn bộ bằng tiếng Anh.
- Mọi text hiển thị cho người dùng (menu, nút, nhãn, tiêu đề, placeholder, thông báo, lỗi) viết bằng tiếng Anh.
- Comment code và tài liệu nội bộ vẫn có thể tiếng Việt, nhưng chuỗi trên UI thì tiếng Anh.
- Ví dụ đúng: `Record and score`, `Target sounds`, `Missing file`.
- Ví dụ sai: `Ghi âm và chấm`, `Phiên âm chuẩn`.

1.2. Cấm em dash trong giao diện.
- Không dùng ký tự em dash (U+2014) hay en dash (U+2013) trong text UI.
- Thay bằng dấu phẩy, dấu hai chấm, hoặc tách thành hai câu.

1.3. Cấm emoji trang trí trong UI.
- Không dùng emoji làm icon chính cho nút, menu, tiêu đề.
- Chỉ cho phép tối đa 1 emoji trong nội dung bài học ví dụ, không dùng trong navigation, button, status, lỗi.
- Thay emoji bằng text rõ nghĩa hoặc icon vector từ thư viện.
- Icon trong web UI phải lấy từ gói `lucide-react`, cấm tự vẽ path/geometry tay cho icon.
- Icon trang trí thuần (streak, biểu đồ, tiêu đề mục) không tính là emoji, nhưng phải là icon từ gói, không phải ký tự emoji.

1.4. Cấm nút thừa, cấm chức năng giả.
- Mọi `Button` render ra màn hình đều phải có `Command` hoặc `Click` hoạt động thật.
- Cấm nút "Coming soon", nút bấm không phản hồi, nút demo cho đẹp.
- Chưa làm xong thì không render. Muốn giữ chỗ thì disable + ghi rõ lý do, ví dụ: "Requires whisper-tiny-encoder.onnx".
- Mỗi luồng chính phải đi hết: bấm -> phản hồi -> kết quả hoặc lỗi dễ hiểu.
- Nhãn nút phải là chữ rõ nghĩa. Cấm nhãn glyph như "x", "◀", "▶". Viết "Remove", "Previous", "Next".
- Nút mà bấm vào chỉ báo lỗi đã biết trước thì phải bị disable kèm lý do, không để bấm rồi mới báo, ví dụ Listening đứng một mình thì không cho Start.

1.5. Cấm từ ngữ thừa, thuật ngữ khoe kỹ thuật.
- UI nói tiếng người dùng, không nói tiếng dev.
- Cấm hiển thị thô các từ: "InferenceSession", "Tensor", "Encoder/Decoder", "Dependency Injection", "DbContext", "NullReference", "StackTrace".
- Lỗi kỹ thuật ghi log, UI chỉ hiện câu ngắn gọn: nguyên nhân + cách sửa.
- Ví dụ sai: "InferenceSession failed: tensor dim mismatch".
- Ví dụ đúng: "The model could not be opened. Check the file in Content/Assets/Models."

1.6. UX phải đơn giản, dùng được ngay.
- Một màn hình chỉ làm một việc chính.
- Sidebar tối đa 7 mục. Bản hiện tại: Overview, Mock Test, Library, Editor, Results, Servers, Settings. Mỗi mục mở ra một panel thật.
- Mọi thao tác tốn quá 1 giây phải có trạng thái chờ: disable nút, hiện "Working...".
- Không bắt người dùng đoán: ô nhập luôn có placeholder và ví dụ.
- Font tối thiểu 13px nội dung, 12px ghi chú. Cấm cỡ chữ nhỏ hơn 12px ở mọi text UI. Tương phản text/nền đủ đọc.
- Ô nhập nhiều dòng (React `<textarea>`) căn chữ từ trên xuống, không căn giữa dọc.
- Hỗ trợ bàn phím cơ bản: Tab theo thứ tự hợp lý, Enter để xác nhận.
- Mỗi mục sidebar phải mở ra một màn hình thật, không trỏ về panel chung.

1.7. Offline-first và ít phụ thuộc, LLM là tùy chọn.
- App phải chạy đầy đủ khi không có model ONNX, không có mạng, không có API key.
- Thiếu file model thì hiện hướng dẫn trong `Content/Assets/Models/README.md`, không crash, không cửa sổ lỗi trắng.
- Web UI chỉ dùng React + Vite + `lucide-react` cho icon. Không thêm framework hay lib UI nặng khác. Biểu đồ vẽ bằng SVG đơn giản, không cần thư viện. Muốn thêm thì ghi rõ lý do và cập nhật mục này.
- Trợ lý AI là phần thêm, không bắt buộc. Không có model ngôn ngữ thì nút AI phải disable kèm câu giải thích, không được crash.
- Không thêm thư viện nặng làm chậm máy yếu. File import/export dùng text, cấm auto-thêm OCR, GPU, hay runtime cồng kềnh.

1.8. Luật gọi AI qua API chuẩn OpenAI.
- Chỉ dùng `ILlmService` cho mọi lời gọi mô hình ngôn ngữ, UI cấm tự dựng `HttpClient`.
- Hỗ trợ mọi server theo chuẩn OpenAI chat completions: OpenAI, Azure-compatible gateway, Ollama, LM Studio, llama.cpp server, vLLM.
- Base URL, tên model, API key, temperature, max tokens để trong Settings, lưu ở `%LocalAppData%/IELTop/settings.json`.
- API key phải mã hóa khi lưu (AES-GCM với key riêng trên máy), cấm ghi key dạng plain text ra file hay log.
- Mọi lời gọi AI phải async, có `CancellationToken`, có xử lý lỗi mạng và timeout ra câu tiếng Anh ngắn gọn.
- Không có key thì app vẫn chạy, chỉ ẩn hoặc disable phần AI.
- Khi Settings đổi, mọi nút AI phải tự bật/tắt ngay qua `RefreshAiState`, cấm để nút AI bật mà gọi thất bại.
- Nếu LLM đã cấu hình nhưng server không chạy, tính năng phải tự rơi về đường offline, không được chặn người dùng.
- Trước khi gọi AI phải qua `LlmConfigValidator` để báo lỗi định dạng (thiếu http, thiếu /v1, thiếu key) ngay trên UI.
- Vision là tùy chọn riêng, mặc định tắt. Chỉ gửi ảnh khi user bật `LlmVisionEnabled`; model không có vision thì không gửi ảnh, cấm gửi payload ảnh rồi báo lỗi.
- Ghi rõ điểm band AI trả về chỉ là ước lượng luyện tập, không phải điểm IELTS chính thức.

1.9. Nguồn dữ liệu chuẩn, không bịa.
- Nội dung học (đề, đáp án, phiên âm, ví dụ) phải lấy từ nguồn mở có giấy phép dùng lại, ghi rõ nguồn.
- Không bịa số liệu, điểm ước lượng, hay kết quả AI khi chưa chạy model.
- Điểm band trong Mock Test chỉ là ước lượng cho luyện nhiều lựa chọn, phải ghi rõ không phải điểm IELTS chính thức.

## 2. Luật code

2.1. Cấu trúc thư mục, đặt đâu làm đó.
- Repo có 3 phần: `IELTop.Core` (dùng chung, đa nền tảng), `IELTop.Desktop` (client Photino + React, đa nền tảng), `Content` (nội dung đọc dùng chung).
- `IELTop.Core/Models/` — entity thuần, không gọi DB, không gọi ONNX, không phụ thuộc nền tảng.
- `IELTop.Core/Data/` — chỉ `AppDbContext` và migration/seed.
- `IELTop.Core/Services/Ai/` — mọi code ONNX và code gọi LLM nằm đây, UI cấm `new InferenceSession` và cấm `HttpClient` trực tiếp.
- `IELTop.Core/Services/Exam/` — engine thi (setup, chấm điểm, review), không phụ thuộc UI.
- `IELTop.Core/Services/App/` — service cho từng màn hình (Library, Editor, Results, Servers, Settings), trả snapshot thuần dữ liệu.
- `IELTop.Core/Services/Audio/`, `IELTop.Core/Services/Storage/` — theo tính năng. Core tối đa `net10.0`, cấm `net10.0-windows`, cấm `System.Windows`, `NAudio`, `System.Speech`, DPAPI trong Core.
- `IELTop.Desktop/Program.cs` — host Photino. `IELTop.Desktop/Bridge/` — router JSON giữa web UI và C#. `IELTop.Desktop/UserInterface/` — React + Vite. `IELTop.Desktop/wwwroot/` — UI đã build, không commit.
- Logic dùng chung phải nằm ở `IELTop.Core`, web UI chỉ gọi qua bridge, không tự tính điểm hay đọc DB.
- `tools/` — công cụ Python (conda `.venv` trong từng thư mục), tách khỏi app C#.
- `Content/Assets/Models/` — model ONNX. `Content/Assets/Exams/` — nội dung JSON. `Content/Assets/Audio/` — file nghe. `Content/Assets/Images/` — ảnh. `Content/servers.txt` — danh sách server cộng đồng.
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
- EF Core: gọi `AppDbContext.EnsureCreated()`, query ngắn gọn, `DbContext` dùng xong dispose.
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
5. Chạy app thử một lần để chắc không crash khi mở cửa sổ. Có thể mở app ẩn và lái qua CDP để kiểm chứng luồng thật.
6. Không commit, không push, không đổi config git khi chưa được yêu cầu.
7. Trả lời ngắn gọn, tiếng Việt, liệt kê file đã đổi dạng `đường_dẫn:dòng`.

## 8. Checklist trước khi báo xong

- [ ] `dotnet build` 0 error.
- [ ] Toàn bộ text UI là tiếng Anh.
- [ ] Không có em dash hoặc en dash trong text UI.
- [ ] Không có emoji mới trong UI.
- [ ] Không có cỡ chữ nhỏ hơn 12px trong CSS.
- [ ] Ô nhập nhiều dòng dùng `<textarea>` căn chữ từ trên.
- [ ] Không có nút bấm chết; mỗi mục sidebar mở ra màn hình thật.
- [ ] Không có nhãn nút kiểu glyph ("x", "◀", "▶").
- [ ] Mọi route bridge khớp với lời gọi `call('...')` trong `UserInterface/src`, không thừa không thiếu.
- [ ] Mỗi `OnnxModelSlot` hiện `Source` và `License` trên màn Settings.
- [ ] Icon dùng `lucide-react`, không vẽ path tay.
- [ ] Không thêm lib UI ngoài React, Vite, lucide-react.
- [ ] Không có thuật ngữ kỹ thuật thô hiện ra UI.
- [ ] App chạy được khi thiếu file `.onnx`, mất mạng, và không có API key.
- [ ] Mọi lời gọi AI đi qua `ILlmService`, không tự dựng `HttpClient` trong UI.
- [ ] API key không lưu plain text; nút AI disable khi chưa cấu hình.
- [ ] Không có LLM thì nhập file vẫn lưu được text, không bị chặn.
- [ ] Nội dung người dùng nhập nằm ở `%LocalAppData%/IELTop/content`, không ở thư mục cài đặt.
- [ ] Mock Test chấm đúng khi submit, có xem lại đáp án, và ghi rõ điểm chỉ là ước lượng.
- [ ] `dotnet list package --vulnerable --include-transitive` và `npm audit` không còn package lỗi.
- [ ] File lớn (model, node_modules, wwwroot) không lọt vào git.

