# UI/UX Redesign Plan v2 — IELTop (chi tiết, không giấu gì)

> BỔ SUNG 2026-09-28 sau review ngoài: nhận cả 5 đề xuất phụ
> (chuột phải Highlight, confirm Submit, transcript rõ hơn, palette
> câu chưa làm, EmptyState Results). Thứ tự làm đổi thành: sticky
> layout trước (giá trị 80%), token sau. Chi tiết ở mục 7.

UI giữ tiếng Anh. File này tiếng Việt.
Ngày lập: 2026-09-28. Chưa code gì theo plan này. Duyệt rồi mới làm.

## 0. Đọc trước: sự thật mất lòng

1. Tôi không chụp màn hình được ở máy này (headless). Mọi sửa giao diện
   tôi chỉ verify bằng build + harness đọc XAML/resource, KHÔNG nhìn
   bằng mắt. Mỗi phase cần bạn mở app chụp lại 2-3 ảnh để chốt.
2. Bug ảnh 8 đã tìm ra gốc thật: style TextBlock toàn app ép chữ đen,
   đè cả chữ trong menu tối và nút xanh (Styles/SimpleTheme.xaml cũ
   dòng 143-145, đã xóa). Không phải do data hay binding.
3. WPF không có Sticky header sẵn. Muốn TimerBar dính khi cuộn thì phải
   mổ layout (mục 4.3), không có cách rẻ.
4. Highlight văn bản thật (bôi vàng đoạn đã chọn) cần RichTextBox, đã
   làm cho passage Reading. Câu hỏi và transcript không highlight được
   vì đang là TextBlock, và sẽ không làm (ít giá trị, nhiều rủi ro).
5. Kéo thả matching đã code (MainWindow.xaml.cs: BankItem_PreviewMouseMove,
   Gap_Drop) nhưng chưa ai vuốt thử thật. Touchscreen có thể không ăn
   vì chỉ nghe sự kiện chuột. Chấp nhận, có fallback: nút x xóa, gõ lại
   bằng cách kéo đè.
6. HandyControl mang theme riêng. Style toàn cục của mình từng đè màu
   chữ của nó. Từ nay cấm style không key (implicit) cho mọi control,
   chỉ style có x:Key.

## 1. Audit hiện trạng có bằng chứng (file:dòng ngày 2026-09-28)

| # | Vấn đề | Bằng chứng | Mức |
|---|--------|-----------|-----|
| 1 | Chữ menu tàng hình trên nền tối | SimpleTheme.xaml cũ 143-145 (đã xóa). Ảnh 8 của bạn | Nghiêm trọng, đã vá gốc |
| 2 | Setup thi dồn 6 cụm vào 1 card tối | MainWindow.xaml 261-320: Paper/Scope/TaskType/Marking/3 checkbox/3 nút trong một Border tối | Cao |
| 3 | TimerBar cuộn mất theo nội dung | Nằm trong ScrollViewer dòng 184, không sticky được nếu không mổ layout | Cao |
| 4 | Nút Submit nằm cuối setup, đang thi phải cuộn mới thấy | Cùng nguyên nhân số 3 | Cao |
| 5 | Câu đang focus không có dấu hiệu riêng | QuestionNavigator chỉ hiện card thường, palette số chưa viền câu focus | Trung bình |
| 6 | Nút Highlight/Clear bé, lẫn với nội dung | Dòng 344-352, nút Ghost nhỏ trong card passage | Trung bình |
| 7 | Results là list chữ dài, không lọc | Dòng 611-656: ItemsControl Attempts thô, không filter/search | Cao |
| 8 | Servers chật, form add lẫn list | Dòng 658-760: 2 cột, form luôn mở | Trung bình |
| 9 | Không biết đề nào đã tải | Không check File.Exists, không badge | Trung bình |
| 10 | Settings 1 cột dài, lỗi validation chìm | Dòng 767-833 | Trung bình |
| 11 | Strict không có dấu hiệu thường trực | Chỉ banner khi đã vi phạm | Trung bình |
| 12 | Tab order/Enter chưa rà | Chưa có IsDefault, chưa test Tab | Thấp |

Đã kiểm tra và KHÔNG sao: `#9CA3AF` chỉ dùng trên nền tối
(14 chỗ: sidebar, setup tối, top bar) nên tương phản ổn, giữ nguyên.
Header 5 trang đã cùng kiểu 28 Bold.

## 2. Design system chốt (ai code cũng theo đúng số này)

### 2.1. Màu và tương phản tính tay (WCAG AA cần 4.5:1 chữ thường)

- Trắng trên `#111827` ~15:1. `#E5E7EB` trên `#111827` ~12:1.
- `#6B7280` trên trắng 4.8:1 (chữ phụ OK).
- Trắng trên `#4F46E5` ~7:1 (nút chính OK).
- Vàng `#F59E0B` chỉ làm viền flagged, không làm chữ.
- Đỏ `#F87171` trên nền tối top bar OK chữ to 22.
- Cấm chữ `#9CA3AF` trên nền trắng (chỉ 2.8:1). Hiện tại không có chỗ
  nào vi phạm, ai thêm mới phải tự check.

### 2.2. Type scale (pt, Segoe UI mặc định, không font ngoài)

28 Bold tiêu đề trang. 22 Bold số timer (Consolas cho khỏi nhảy số).
16 SemiBold tên test setup. 15 SemiBold tiêu đề section. 14 nội dung,
câu hỏi, ô nhập. 13 transcript, notes. 12-13 ghi chú, tối thiểu 12.
Sidebar nav 15, footer 14/11.

### 2.3. Spacing

Card padding 18, bo 12, cách 12. Nút chính 14x8, nút nhỏ 12x6 và 10x4,
bo 8. Trang padding 28/24, nội dung MaxWidth 960 (hiện 920, nới lên).
Sidebar 264, item cao tối thiểu 48, cách 4.

### 2.4. Component chuẩn, mỗi loại đúng 1 style

PrimaryButton, GhostButton có sẵn giữ. THÊM DangerButton nền `#B91C1C`
chữ trắng cho Clear all, Remove server. THÊM Badge (Border bo 12,
padding 8x3, chữ 11 SemiBold) với 4 màu: xanh Ready/Downloaded, xám
Missing, vàng Flagged, đỏ Violations. THÊM EmptyState (TextBlock lớn
màu nhạt + 1 câu + 1 nút). Card giữ, thêm CardFlat không bóng cho list
con để đỡ nặng GPU (DropShadowEffect nhân lên hàng chục card sẽ giật
máy yếu, đây là lý do một số màn hình lag).

### 2.5. Icon nav tự vẽ (không emoji, không font lạ)

Mỗi icon là Canvas 16x16 đặt cạnh chữ trong template NavItem, màu ăn
theo Foreground của item qua
`Fill="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ListBoxItem}}"`
(viền dùng Stroke binding tương tự). Hình cụ thể:

- Overview: 4 Rectangle 6x6 tại (1,1) (9,1) (1,9) (9,9).
- Mock Test: Ellipse 13x13 viền 2 tại (1.5,1.5) + Line kim (8,8)-(8,4)
  và (8,8)-(11,9).
- Results: Ellipse huy chương tâm (8,6) r4 + Polygon ruy băng
  điểm (5,10) (11,10) (9,15) (7,15).
- Servers: Rectangle Outline 12x13 tại (2,1) + 2 Line ngang bên trong
  y=5 và y=9 từ x=4 tới x=12.
- Settings: 3 Line ngang y=3,8,13 từ x=1 tới x=15 + 3 Ellipse r2 làm
  núm tại (5,3) (11,8) (7,13).

## 3. Shell

Sidebar giữ 264px, không thu gọn. Logo + nav 5 mục có icon + badge số
(số đề Papers.Count, số kết quả ExamCount — MainViewModel đã có 2 số
này, chỉ việc bind thêm TextBlock nhỏ cạnh Content). Footer Models và
Language model giữ, chữ đã sáng. NavItem giữ gạch accent khi chọn
(đã làm), icon ăn màu theo item.

## 4. Từng màn hình: code cũ -> code mới chính xác

### 4.1. Overview

- Thêm hàng 3 thẻ số: Finished tests (ExamCount), Last band range
  (LastBand), Papers ready (Papers.Count — THÊM property này vào
  MainViewModel, 3 dòng).
- How it works viết lại 4 dòng đánh số, 2 nút Start a test
  (CurrentPage=Exam) và Browse servers (CurrentPage=Servers). Nút chỉ
  set property, không logic mới.
- Models: mỗi dòng thêm Badge State + nút Unload đã có. Giữ dòng RAM.

### 4.2. Setup thi (MainWindow.xaml 261-320)

Chia card tối thành 3 card sáng Step 1/2/3 như plan v1, giữ nguyên mọi
binding (Papers, Scopes, TaskTypes, StrictnessOptions, StrictMode,
ShuffleParts, MixAllPapers, BuildModes). Nút Start test Primary to,
IsDefault=True. Nút Submit chuyển lên TimerBar (mục 4.3), ở setup chỉ
hiện khi IsRunning (giữ binding cũ).

### 4.3. Mổ layout để TimerBar sticky (phần khó nhất, nói thẳng)

Hiện tại ScrollViewer dòng 184 bọc tất cả nên mọi thứ cuộn mất. Cách
làm: trong StackPanel Exam IsRunning, tách thành Grid 3 hàng
(Auto/*/Auto): hàng 1 TimerBar (+banner strict/violation), hàng 2 là
ScrollViewer bọc nội dung part (giữ nguyên các card, chỉ chuyển vào),
hàng 3 là thanh palette (chuyển nguyên khối palette hiện tại xuống).
Việc này di chuyển khoảng 200 dòng XAML, rủi ro kẹp thẻ sai cao nên
sau khi sửa phải build ngay và đối chiếu từng thẻ đóng. Nút Submit đặt
trong TimerBar phải, luôn thấy. Banner strict thường trực: chấm đỏ +
chữ Strict mode ON khi StrictMode (mới), banner violations giữ.

### 4.4. Câu hỏi

- Card câu đang focus viền accent 2px: bọc ExamQuestionTemplate Border
  bằng DataTrigger so FocusedIndex? Template không biết index của
  mình. Cách rẻ và thật: ViewModel thêm `IsFocused` cập nhật trong
  OnFocusedIndexChanged (set false hết, true cho câu focus), XAML
  DataTrigger IsFocused đổi BorderBrush. Thêm 1 property, 5 dòng code.
- Số palette của câu focus: viền accent 3px qua DataTrigger IsFocused
  trong PaletteButton (Style đã có, thêm trigger).
- Nút Highlight/Clear: lên Padding 12x6, thêm hint "Bôi đen đoạn văn
  rồi bấm Highlight" ngay trên nút.
- Gap/match giữ nguyên logic, chỉ ăn theme mới.

### 4.5. Result và Results

- Result hero: Band range 32 Bold + Score + scope/strictness/violations
  + 3 nút Grade/Review/Back to setup (MỚI BackToSetupCommand: reset
  IsRunning=false, IsFinished=false, CurrentPage giữ Exam, ScrollTop?
  Không cuộn được bằng binding, chấp nhận ở yên vị trí).
- Review mỗi dòng có Path icon ✓/✗ thay chữ. Path data cố định:
  ✓ `M4,12 L10,18 L20,6` stroke `#15803D` 2px; ✗ `M6,6 L18,18 M18,6 L6,18`
  stroke `#B91C1C`.
- Results: thêm ComboBox lọc scope (All + Full test/Reading/Writing/
  Speaking) + TextBox tìm tên đề. Lọc client trên Attempts đã load
  (THÊM FilteredAttempts ObservableCollection + ApplyFilter trong
  ResultsViewModel, copy mẫu ServersViewModel). Card attempt thêm badge
  violations đỏ khi >0. Bảng tiêu chí cho vào Expander thu gọn.

### 4.6. Servers

- Card server có nút Connect ngay trên card, bỏ nút Connect chung.
- Form Add cho vào Expander đóng mặc định.
- Badge Downloaded xanh khi File.Exists(ExamsDir/SafeFileName(id).json).
  Cần THÊM `IsDownloaded(id)` vào ServersViewModel (1 hàm File.Exists)
  và refresh sau khi Save (gọi lại ApplyFilter là đủ vì binding đọc
  trực tiếp).
- Remove saved dùng DangerButton.

### 4.7. Settings

- 3 card nhóm: Language model / Display / About. Display MỚI: ComboBox
  Text size Normal/Large -> ExamViewModel.FontScale 1.0/1.15.
  FontScale áp qua MultiplyConverter MỚI (ValueConverters.cs, 15 dòng)
  cho đúng 6 chỗ chữ thi chính: passage, prompt, essay, transcript,
  material, options (FontSize="{Binding Exam.FontScale, Converter=...}"
  không nhân được trực tiếp nên converter nhận parameter là cỡ gốc:
  `{Binding Exam.FontScale, Converter={StaticResource ScaleFont},
  ConverterParameter=14}`). DataContext trong template là part/question
  nên phải RelativeSource về Exam như palette đang làm.
- Validation viền đỏ: Style TextBox có sẵn của HandyControl đã đỏ khi
  lỗi? Không chắc. Cách thật: TextBlock lỗi đỏ dưới ô, binding Problems
  có sẵn, chỉ cần gom lại gần ô thay vì liệt kê xa. Giữ đơn giản.
- About: version từ Assembly (1 dòng code), nút Open data folder
  (Process.Start đường dẫn LocalAppData/IELTop), nút Reset settings
  (XÓA settings.json + reload? SettingsStore không có Reload — THÊM
  Reload() đọc lại file, 10 dòng).

## 5. Luồng và bàn phím

- Tab order: setup trên xuống, Start cuối (WPF mặc định theo thứ tự
  XAML, kiểm tra lại sau khi chia card).
- IsDefault: Start test, Connect, Save paper? (Save nằm trong template,
  IsDefault trong template khó — bỏ qua, chỉ Start/Connect).
- Enter nộp bài khi focus ngoài ô essay: không ép (dễ nộp nhầm), giữ nút.
- Mọi thao tác >1s đã có LoadingCircle + disable, rà lại AI pick và
  TTS save (2 chỗ thiếu).

## 6. Rủi ro nói thẳng và cách đỡ

1. Mổ layout TimerBar/palette: dễ kẹp thẻ. Đỡ bằng build sau mỗi khối
   di chuyển, diff từng thẻ đóng.
2. RichTextBox + binding Material đổi part: document rebuild mất
   highlight cũ (chấp nhận, đúng thi thật cũng vậy).
3. Kéo thả chuột thật chưa ai vuốt: giữ nút x + kéo đè, ghi hint.
4. HandyControl theme: style mình chỉ chạm Button/ListBoxItem/TextBox
   có Key, không động vào template của nó.
5. Card DropShadow trên máy yếu: CardFlat cho list con (palette số,
   bank item, attempt rows).
6. FontScale binding RelativeSource trong template lồng nhau dễ sai
   AncestorType: test build + harness đọc XAML kiểm tra chuỗi
   "ScaleFont" tồn tại và converter tồn tại.
7. Tôi không nhìn được màn hình: mỗi phase bạn chụp 3 ảnh (setup, đang
   thi, result) để chốt. Không có ảnh tôi không dám bảo đẹp.

## 7. Phase và nghiệm thu (thứ tự theo review ngoài: sticky trước)

- Phase A Sticky layout + Submit (ĐÃ XONG 2026-09-28): Grid 3 hàng,
  Submit + Strict pill trên TimerBar, banner violation dính, confirm
  Submit kèm đếm câu trống. Build xanh.
- Phase B Token + Shell (ĐÃ XONG 2026-09-28): DangerButton, 4 Badge
  (Green/Gray/Amber/Red), CardFlat, icon nav tự vẽ 5 mục + badge số đề
  và số kết quả, CountToVisibility. Build xanh.
- Phase C supplements (ĐÃ XONG 2026-09-28): palette câu chưa làm nền
  `#F5F6FA` chữ `#6B7280`, ContextMenu chuột phải Highlight, transcript
  placeholder rõ + chấm đỏ recording, Results EmptyState + GoExam.
- Phase D Result + Results (ĐÃ XONG 2026-09-28): nút Back to setup,
  review icon ✓✗ Path data đúng plan, Results filter scope + tìm tên,
  violations badge đỏ, criteria vào Expander thu gọn.
- Phase E Servers + Settings (ĐÃ XONG 2026-09-28): Connect trên từng
  card server, form Add vào Expander đóng mặc định, badge Downloaded
  xanh live (RemotePaperRow), Remove dùng DangerButton; Settings thêm
  Display cỡ chữ (FontScale 1.0/1.15 qua ScaleFontConverter, 7 chỗ chữ
  thi) + About (version, data folder, mở folder, Reset settings).
- Phase G Bulk import/export + multi-download + AI check (ĐÃ XONG
  2026-09-28): PaperValidator thuần (title, parts, skill, minutes, số
  câu, options/key, gap answer, match bank, unknown kind, cap 20 lỗi);
  Import nhiều file JSON có validate và chống ghi đè; Export danh sách
  lọc ra thư mục timestamp kèm audio; Download all listed có tiến
  trình và Stop; paper tải về validate trước khi lưu; AI check paper
  qua ILlmService (ReviewPaperAsync, JSON score/strengths/fixes).
  Harness 27/27 pass. Build xanh.
- Phase F còn lại: Tab order/IsDefault rà tay khi có màn hình,
  checklist AGENTS.md cuối, harness binding audit mở rộng.

Checklist chung mỗi phase: build 0 error 0 warning, text Anh, không em
dash/emoji, không nút chết, offline vẫn chạy, không file lớn vào git.
