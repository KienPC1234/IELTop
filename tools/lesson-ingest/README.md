# lesson-ingest

Chuẩn hóa gói bài giảng (`Lesson Content`) thành mỗi Unit một file JSON trong
`Content/Assets/Lessons/`. Giữ nguyên chữ của giáo viên, chỉ thêm cấu trúc; file
không đọc được thì ghi vào `ingest-report.md`, không bỏ im lặng.

## Môi trường

Từ thư mục `tools/lesson-ingest`:

```
conda env create -p .venv -f environment.yml
conda activate .\.venv
```

## Chạy

```
python ingest.py --src "<đường dẫn Lesson Content>" --out ../../Content/Assets/Lessons --audio ../../Content/Assets/Audio
```

Kết quả:

- `Content/Assets/Lessons/unit-01.json` ... mỗi Unit một file.
- `Content/Assets/Lessons/ingest-report.md` liệt kê số file đọc được và cảnh báo.
- `Content/Assets/Audio/Lessons/<unit>/Audio x.y.mp3` khi có `--audio`. Thư mục này
  bị `.gitignore` (file lớn không vào git); chạy lại tool sau khi clone để có audio.

## Định dạng mỗi file

```jsonc
{
  "unit": "Unit 1",
  "source": "IELTop lesson content. Used and redistributed with the owner's permission.",
  "files": ["U1 Grammar.docx", "U1 Keys/U1 Grammar Keys.docx", "Audio/Audio 1.1.mp3"],
  "audio": ["Lessons/unit-1/Audio 1.1.mp3"],
  "sections": [
    {
      "id": "u1-grammar",
      "skill": "Grammar",
      "title": "U1 Grammar",
      "isAnswerKey": false,
      "blocks": [
        { "type": "text", "text": "..." },
        { "type": "table", "rows": [["Verb", "Action Verb"], ["play", "x"]] }
      ]
    }
  ],
  "vocabulary": [
    {
      "word": "ecologist",
      "form": "noun",
      "meaning": "nhà sinh thái học",
      "example": "an ecologist at the University of California, Davis.",
      "extraExample": "The ecologist studied rainforest insects.",
      "ipa": "/ɪˈkɒlədʒɪst/",
      "derivatives": "ecology (n), ecological (adj)"
    }
  ],
  "slides": [
    { "file": "Unit 1.pptx", "slides": [{ "index": 1, "text": "UNIT 1 ..." }] }
  ]
}
```

Bước đọc file bằng thư viện Python: `.docx` = python-docx, `.pptx` = python-pptx,
`.xlsx` = openpyxl, `.pdf` = pypdf. Audio chỉ được liệt kê; phần copy sang
`Content/Assets/Audio/` do người chạy quyết định để tránh nhân bản file lớn.
