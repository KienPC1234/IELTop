# IELTop — Mô tả giao diện hiện tại (để phân tích)

Tiếng Việt. Mô tả đúng code tại `IELTop/MainWindow.xaml` (834 dòng),
`IELTop/Styles/SimpleTheme.xaml`, `IELTop/MainWindow.xaml.cs`.
Ngày ghi: 2026-09-28.

## 1. Khung cửa sổ

- `Window` tiêu đề IELTop, mock test only, 1240x780, tối thiểu 1040x640,
  giữa màn hình, nền `#F5F6FA`.
- Lưới 2 cột. Cột trái sidebar 252px nền `#111827`, padding 14/20.
  Cột phải là `ScrollViewer` cuộn dọc duy nhất, padding 28/24, nội dung
  `StackPanel MaxWidth=920`.
- 5 trang chuyển qua `CurrentPage` + converter PageMatch: Overview,
  Mock Test (tag Exam), Results, Servers, Settings.

## 2. Sidebar (trái, nền tối, luôn hiện)

- Logo: IELTop 24 Bold trắng + dòng Mock test only 12 `#9CA3AF`.
- Nav `ListBox` 5 mục chữ 15 màu `#E5E7EB`, padding 14/12, cách nhau 4px.
  Mục chọn: nền `#1F2937` + gạch accent `#4F46E5` dọc trái + chữ trắng
  SemiBold. Hover: nền `#1F2937` chữ trắng. Không icon.
- Chân sidebar: MODELS + dòng `ModelsSummary` trắng 14; LANGUAGE MODEL
  + dòng `LlmSummary` trắng 14 wrap.

## 3. Theme token (`Styles/SimpleTheme.xaml`)

- Accent `#4F46E5`, hover `#4338CA`, nhấn `#3730A3`. Chữ đen `#111827`,
  chữ phụ `#6B7280`. Viền `#E5E7EB`. Nền `#F5F6FA`, card trắng.
- PrimaryButton: nền accent chữ trắng, hover đậm, nhấn `#3730A3`,
  disable nền `#C7CBD3`. Chữ kế thừa đúng nhờ TextElement binding.
- GhostButton: trong suốt viền xám chữ đen, hover nền `#F3F4F6`,
  disable chữ `#9CA3AF`.
- Card: trắng, bo 12, padding 18, viền xám, bóng đổ nhẹ.
- Chữ: tiêu đề trang 28 Bold, SectionTitle 15 SemiBold, StatValue 30
  Bold, StatLabel 12 xám.
- Converter: Bool2Visibility, InverseBool, InverseBool2Visibility,
  PageMatch, String2Visibility. Không còn style TextBlock toàn cục
  (đã xóa vì từng làm chữ tàng hình, xem ảnh 8 cũ).

## 4. Overview

- Tiêu đề 28 + dòng phụ xám.
- 2 thẻ số: Finished tests (ExamCount, số 30) và Last band range
  (LastBand, số 26) kèm chú thích range chỉ ước lượng.
- Thẻ How it works: 1 đoạn 4 bước chữ 13 + ModelsSummary + LlmSummary
  + câu offline + nút Refresh (Ghost, trái).
- Thẻ Offline models: đường dẫn folder Consolas 12 + dòng LoadedMemory
  (RAM đang giữ) + mỗi model 1 hàng: Skill tím 13 + State 11 xám,
  FileDetail 11, Message 11, 2 nút Load/Unload (Ghost).

## 5. Mock Test

### 5.1. Setup (card tối `#111827`, chữ xám `#9CA3AF` + trắng)

- Tiêu đề Test setup trắng 16. Lưới 2 cột.
- Trái: Paper (ComboBox + placeholder), Scope (Full test/Reading/
  Writing/Speaking), Task type (All types + loại trong đề), ghi chú
  Listening chỉ chạy full test 11px.
- Phải: Marking level (Lenient/Standard/Strict), CheckBox Strict mode,
  chữ Build + ComboBox Test assembly (Paper order/Random/AI pick),
  CheckBox Shuffle parts, CheckBox Mix parts from all papers.
- Dòng StrictModeHint + hàng nút Start test / Submit (Primary) +
  Topic idea with AI (Ghost, disable khi không AI) + StatusMessage +
  hint thêm model khi chưa có.
- Card vàng cảnh báo khi chưa có đề.

### 5.2. Thanh thi (nền tối, hiện khi IsRunning)

- Trái: tên part trắng 14 cắt chữ. Giữa: Time left + timer 22 Bold
  trắng, cam `#FBBF24` dưới 10 phút, đỏ `#F87171` dưới 5 phút. Phải:
  Volume slider 120px khi Listening.
- Banner đỏ `#7F1D1D` khi vi phạm strict: Stay in the test...

### 5.3. Nội dung part

- Card thông tin: HeaderLine tím 12 (tên đề | skill | loại | chủ đề),
  Instructions xám 12, ghi chú Speaking/Listening 12.
- Reading chia đôi: trái passage RichTextBox chỉ đọc 13px cao tối đa
  420 + nút Highlight/Clear (Ghost nhỏ) + hint; phải câu hỏi.
- Listening: card Audio + nút Start now (Primary, khi chờ đọc đề) +
  AudioStatus + transcript chỉ khi mất tiếng + câu transcript ở review.
- Writing: ô essay 14 cao 250 + WordCountLabel + câu AI chấm sau nộp.
- Speaking: Record answer (Primary) + Finish part (Ghost) +
  AudioStatus + ô gõ lại transcript 14 cao 120 + số từ.
- Câu hỏi focus: 1 card gồm NumberLabel tím 12 + CheckBox Flag, Prompt
  14, panel choice (radio A/B/C/D) hoặc gap (ô nhập 14 + hint chính tả)
  hoặc match (bank kéo thả + từng dòng Label + ô Drop + nút x).
- Dưới câu hỏi: Previous/Next question (Ghost) + ProgressLabel
  (Answered x of y, flagged z) + card Notes (ô 13 cao 70).
- Thanh palette cuối: ScrollViewer ngang, mỗi part 1 cụm TabLabel +
  các số 36x32 (trắng viền xám; đã trả lời nền tím chữ trắng; flagged
  viền vàng 2px; câu focus CHƯA có dấu hiệu riêng), nút ◀ ▶ chuyển part.

### 5.4. Result (sau Submit)

- Card Result: Band range 24 Bold tím + ResultText 13 + CriteriaHint 12
  + hàng nút Grade with AI (Primary, disable khi không AI) + Show
  answer review (Ghost) + hint thêm model.
- AI feedback và ReviewLines: list chữ 13, Review hiện cả transcript
  Listening và chi tiết từng dòng match.

## 6. Results

- Tiêu đề + SummaryLabel + nút Reload + Clear all (Ghost).
- Mỗi attempt 1 card: tên đề 14 SemiBold, dòng scope | strictness |
  ngày giờ 12 xám, band 20 Bold tím, Summary 12 xám. Không lọc, không
  xem chi tiết, không hiện violations riêng (nằm trong Summary).
- Card Official criteria: hint + list Writing/Speaking band + câu
  practice estimate.

## 7. Servers (2 cột bằng nhau)

- Trái: list server dạng nút Ghost full-width (tên 13 + URL 11 + Detail
  11) + Refresh list + Remove saved + form Add (Name, URL, AuthModes,
  Username, Secret, CheckBox AllowInsecure, nút Add server Primary) +
  câu key mã hóa 11px.
- Phải: ServerInfo + Connect (Primary) + Search + Categories combo +
  mỗi paper 1 card (Title 13 + Summary 11 + nút Save) + LoadingCircle
  22 + StatusMessage 13.
- Chưa có: badge Downloaded, nút Connect trên từng card, Expander form.

## 8. Settings (1 cột dài)

- Card Language model: mô tả 12 + Base URL / Model name / API key
  (placeholder, padding 8) + key mã hóa 11px + Temperature + Max reply
  (NumericUpDown) + 2 CheckBox streaming/vision + hint vision 11px.
- Card Check: CheckSummary 12 + list Problems 12 + nút Save (Primary) +
  Test connection (Ghost) + LoadingCircle + StatusMessage 13 + card
  Connection details Consolas 12.
- Card What works without a model + card Example local setups
  (Ollama/LM Studio/OpenAI) nền `#EEF2FF` viền `#C7D2FE`.

## 9. Hành vi đặc biệt (code behind + VM)

- Strict mode: full screen borderless topmost, chặn F11/F12/Win/Apps/
  PrintScreen/Alt+Tab/Alt+F4/Esc/Ctrl+Shift+Esc (best effort, Alt+Tab
  OS vẫn thắng), hỏi khi đóng giữa giờ, đếm Deactivated thành
  Violations lưu DB + hiện banner.
- Highlight passage: click code behind bôi vàng selection, Clear dựng
  lại document (mất highlight cũ khi đổi part).
- Kéo thả match: PreviewMouseMove kéo chữ, Drop thả vào ô, nút x xóa.
  Chưa ai vuốt thật, touchscreen có thể không ăn.
- Timer DispatcherTimer 1 giây, hết part tự sang, hết bài tự nộp.
  Listening chờ đọc 15 giây (SkipPrep bỏ qua), phát 1 lần, TTS đọc
  transcript khi thiếu file, giọng Windows khi thiếu model.
- Chuyển trang tự refresh: Exam load đề, Results load điểm, Overview
  load số.

## 10. Vấn đề đã biết (chưa sửa)

1. Số palette của câu đang focus không có dấu hiệu riêng.
2. Results không lọc/tìm, violations chìm trong Summary.
3. Form Add server luôn mở chiếm chỗ, chưa có badge Downloaded.
4. Settings validation liệt kê xa ô nhập, chưa viền đỏ tại ô.
5. Chưa có IsDefault/Tab order rà soát, chưa tên trợ năng.
6. Không skeleton loading, chỉ LoadingCircle + chữ.
7. Header các trang đã đồng nhất 28 Bold (ổn, giữ).
