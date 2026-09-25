# speaking-mdd - xuất model chấm phát âm

Công cụ Python tách riêng để tạo model ONNX cho phần Luyện nói của IELTop.
App C# chỉ nạp file kết quả, không cần cài Python khi chạy.

## Vì sao cần model riêng

Whisper gốc không phù hợp để bắt lỗi phát âm vì nó tự sửa theo ngữ cảnh,
trả về từ đúng thay vì lỗi thật của người học. Bài toán này giải ở mức âm vị
(phoneme) bằng model tự giám sát Wav2Vec2/HuBERT/WavLM fine-tune với CTC,
rồi gióng hàng với phiên âm chuẩn để tìm lỗi thay thế / bỏ sót / thêm âm.

## Cài môi trường (conda, đặt trong .venv của thư mục này)

```powershell
conda env create -p .venv -f environment.yml
conda activate .\.venv
```

## Xuất model

```powershell
python export_onnx.py --out ./models
```

Mặc định dùng checkpoint CTC cấp âm vị
`bobboyms/wav2vec2-base-en-phoneme-ctc-41h` (Apache-2.0), trả về chuỗi âm vị
tiếng Anh thay vì ký tự. Đổi checkpoint khác bằng `--model` nếu muốn.

Script in ra kích thước FP32 và INT8, đồng thời chạy thử một lượt suy luận
trên CPU để xác nhận model hoạt động trước khi dùng.

## Kiểm tra khớp hệ ký hiệu âm vị

Model phát ra âm vị theo IPA (`θ`, `ɪ`, `ŋ`), không phải ARPABET (`TH`, `IH`, `NG`).
`phoneme-map.json` phải dùng cùng IPA, nếu không điểm chấm sẽ luôn sai.
Sau khi xuất model, chạy kiểm tra:

```powershell
python check_phoneme_map.py
```

Script báo mọi âm trong map không có trong bộ nhãn của model.

## Kết quả và cách dùng trong app

Trong `./models` sẽ có:

| File | Dùng cho |
|------|----------|
| `mdd-wav2vec2-base-int8.onnx` | model nhận âm vị, chạy CPU |
| `mdd-labels.json` | bảng nhãn khớp đúng model, app đọc để giải mã |
| `mdd-labels.txt` | danh sách nhãn dùng được, để đối chiếu với `phoneme-map.json` |

Chép 2 file đầu tiên vào `IELTop/Assets/Models/`, mở app, vào mục Luyện nói.
Chưa có model thì app vẫn chạy và báo thiếu file rõ ràng, không crash.

## Mức tài nguyên trên CPU phổ thông

| Phiên bản | File | RAM | 3 giây audio |
|-----------|------|-----|--------------|
| Wav2Vec2-Base FP32 | ~378 MB | ~1 GB | 0.5 - 1.0 s |
| Wav2Vec2-Base INT8 | ~122 MB | ~0.3 GB | ~0.12 s |

Client chỉ dùng bản Base INT8. Luôn giới hạn audio ngắn (dưới 5 giây mỗi lượt).
Phía C#, `session.Run` chạy trong `Task.Run` nên giao diện không bị đơ.

Lưu ý kỹ thuật: onnxruntime không lượng tử hóa được lớp Conv trong
`pos_conv_embed` của wav2vec2, nên script chỉ lượng tử hóa `MatMul` và `Gemm`.
Phần này chiếm gần hết trọng số và vẫn cho kết quả giải mã trùng khớp với bản FP32.
