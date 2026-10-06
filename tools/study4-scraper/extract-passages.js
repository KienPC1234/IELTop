const { chromium } = require('playwright-core');
const fs = require('fs');
const path = require('path');

const OUTPUT_FILE = path.join(__dirname, 'consolidated_readings.json');

const COOKIES = [
  { name: "_fbc", value: "fb.1.1779713453051.IwY2xjawSBJPlleHRuA2FlbQIxMABicmlkETF3cFdBdGRzeXZmeE4zMzE0c3J0YwZhcHBfaWQQMjIyMDM5MTc4ODIwMDg5MgABHiDCwhkprL9jtHPW2B53YzXzzv0GuIB2XdxrpL1ettOzB3NtN8mwy19D2aFy_aem_Ay7TePJtYusOPCDC5rnUMw", domain: ".study4.com", path: "/" },
  { name: "_ga", value: "GA1.1.89434967.1773842268", domain: ".study4.com", path: "/" },
  { name: "_fbp", value: "fb.1.1773842268155.733205933806791136", domain: ".study4.com", path: "/" },
  { name: "_ga_64Z8KN7V8D", value: "GS2.1.s1791207958$o38$g0$t1791207958$j60$l1$h906419549$d1jQaZM2dM4N7wErdWfleORsE1lXrKgMSXw", domain: ".study4.com", path: "/" },
  { name: "_gcl_au", value: "1.1.1641912758.1787059062.824289880.1790869262.1790869915.1391891997.1790869262.1790869915", domain: ".study4.com", path: "/" },
  { name: "csrftoken", value: "bI8x32CWoOCHObiVucQnwGvUr2S6GxJy", domain: "study4.com", path: "/" },
  { name: "sessionid", value: "v1vqfnwjxpsu3ko0r1yhhqzgv4s8dm1u", domain: "study4.com", path: "/" }
];

// Master list of all reading passages required for Unit 1 to Unit 10
const READING_TARGETS = [
  // Unit 1
  {
    unit: "unit-1",
    id: "ocean-garbage",
    title: "How Bad Is Ocean Garbage, Really?",
    cambridgeRef: "Cam 14.4 Passage 3",
    url: "https://ieltstrainingonline.com/practice-cam-14-reading-test-04-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 3
  },
  // Unit 2
  {
    unit: "unit-2",
    id: "megafires-california",
    title: "The megafires of California",
    cambridgeRef: "Cam 10.4 Passage 1",
    url: "https://study4.com/tests/1163/the-megafires-of-california/solutions/",
    type: "study4"
  },
  {
    unit: "unit-2",
    id: "pulling-strings-pyramids",
    title: "Pulling Strings to Build Pyramids",
    cambridgeRef: "Cam 7.4 Passage 1",
    url: "https://study4.com/tests/1148/pulling-strings-to-build-pyramids/solutions/",
    type: "study4"
  },
  {
    unit: "unit-2",
    id: "raising-mary-rose",
    title: "Raising the Mary Rose",
    cambridgeRef: "Cam 11.2 Passage 1",
    url: "https://ieltstrainingonline.com/practice-cam-11-reading-test-02-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 1
  },
  {
    unit: "unit-2",
    id: "cutty-sark",
    title: "Cutty Sark - the fastest sailing ship of all time",
    cambridgeRef: "Cam 13.4 Passage 1",
    url: "https://ieltstrainingonline.com/practice-cam-13-reading-test-04-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 1
  },
  // Unit 3
  {
    unit: "unit-3",
    id: "tourism-new-zealand",
    title: "Case Study: Tourism New Zealand website",
    cambridgeRef: "Cam 13.1 Passage 1",
    url: "https://ieltstrainingonline.com/practice-cam-13-reading-test-01-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 1
  },
  {
    unit: "unit-3",
    id: "dead-sea-scrolls",
    title: "The Dead Sea Scrolls",
    cambridgeRef: "Cam 17.2 Passage 1",
    url: "https://ieltstrainingonline.com/practice-cam-17-reading-test-02-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 1
  },
  {
    unit: "unit-3",
    id: "cork",
    title: "Cork",
    cambridgeRef: "Cam 12.1 Passage 1",
    url: "https://study4.com/tests/1084/cork/solutions/",
    type: "study4"
  },
  {
    unit: "unit-3",
    id: "history-of-glass",
    title: "The History of Glass",
    cambridgeRef: "Cam 12.4 Passage 1",
    url: "https://study4.com/tests/1093/the-history-of-glass/solutions/",
    type: "study4"
  },
  {
    unit: "unit-3",
    id: "silbo-gomero",
    title: "Silbo Gomero: the whistle 'language' of the Canary Islands",
    cambridgeRef: "Cam 15.4 Passage 2",
    url: "https://ieltstrainingonline.com/practice-cam-15-reading-test-04-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 2
  },
  // Unit 4
  {
    unit: "unit-4",
    id: "white-horse-uffington",
    title: "The White Horse of Uffington",
    cambridgeRef: "Cam 16.2 Passage 1",
    url: "https://ieltstrainingonline.com/practice-cam-16-reading-test-02-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 1
  },
  {
    unit: "unit-4",
    id: "story-of-silk",
    title: "The Story of Silk",
    cambridgeRef: "Cam 11.3 Passage 1",
    url: "https://study4.com/tests/1096/the-story-of-silk/solutions/",
    type: "study4"
  },
  {
    unit: "unit-4",
    id: "roman-shipbuilding",
    title: "Roman Shipbuilding and Navigation",
    cambridgeRef: "Cam 16.3 Passage 1",
    url: "https://ieltstrainingonline.com/practice-cam-16-reading-test-03-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 1
  },
  // Unit 5
  {
    unit: "unit-5",
    id: "oxytocin",
    title: "Oxytocin",
    cambridgeRef: "Cam 13.2 Passage 2",
    url: "https://study4.com/tests/2021/ielts-simulation-reading-test-7/solutions/",
    type: "study4-oxytocin"
  },
  {
    unit: "unit-5",
    id: "health-sciences-geography",
    title: "The Intersection of Health Sciences and Geography",
    cambridgeRef: "Cam 12.3 Passage 2",
    url: "https://study4.com/tests/1047/the-intersection-of-health-sciences-and-geography/solutions/",
    type: "study4"
  },
  {
    unit: "unit-5",
    id: "the-thylacine",
    title: "The Thylacine",
    cambridgeRef: "Cam 17.1 Passage 1",
    url: "https://study4.com/tests/62/recent-ielts-reading-actual-test-13/solutions/",
    type: "study4"
  },
  // Unit 6
  {
    unit: "unit-6",
    id: "little-ice-age",
    title: "The Little Ice Age",
    cambridgeRef: "Cam 8.2 Passage 2",
    url: "https://study4.com/tests/1120/the-little-ice-age/solutions/",
    type: "study4"
  },
  {
    unit: "unit-6",
    id: "easter-island",
    title: "What destroyed the civilisation of Easter Island?",
    cambridgeRef: "Cam 17.1 Passage 2",
    url: "https://study4.com/tests/1156/what-destroyed-the-civilisation-of-easter-island/solutions/",
    type: "study4"
  },
  // Unit 7
  {
    unit: "unit-7",
    id: "natural-architecture",
    title: "The Recovery of Natural Environments in Architecture",
    cambridgeRef: "Cam 13.3 Passage 2",
    url: "https://ieltstrainingonline.com/practice-cam-13-reading-test-03-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 2
  },
  // Unit 8
  {
    unit: "unit-8",
    id: "iconoclastic-brain",
    title: "A neuroscientist reveals how to think differently (Iconoclasts)",
    cambridgeRef: "Cam 9.2 Passage 3",
    url: "https://study4.com/tests/1180/a-neuroscientist-reveals-how-to-think-differently/solutions/",
    type: "study4"
  },
  {
    unit: "unit-8",
    id: "educating-psyche",
    title: "Educating Psyche",
    cambridgeRef: "Cam 7.1 Passage 3",
    url: "https://ieltsdeal.com/ielts-academic-reading-cambridge-7-test-1-reading-passage-3-educating-psyche-with-top-solutions-and-step-by-step-detailed-explanations/",
    type: "ieltsdeal"
  },
  {
    unit: "unit-8",
    id: "thinking-by-numbers",
    title: "Book Review: Thinking By Numbers",
    cambridgeRef: "Cam 13.4 Passage 3",
    url: "https://study4.com/tests/2013/ielts-simulation-reading-test-3/solutions/",
    type: "study4"
  },
  {
    unit: "unit-8",
    id: "wise-decisions",
    title: "How to make wise decisions",
    cambridgeRef: "Cam 16.2 Passage 3",
    url: "https://ieltstrainingonline.com/practice-cam-16-reading-test-02-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 3
  },
  // Unit 9
  {
    unit: "unit-9",
    id: "big-businesses",
    title: "Environmental practices of big businesses",
    cambridgeRef: "Cam 15.2 Passage 3",
    url: "https://ieltstrainingonline.com/practice-cam-15-reading-test-02-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 3
  },
  {
    unit: "unit-9",
    id: "european-transport",
    title: "Trends and prospects for European transport systems 1990-2010",
    cambridgeRef: "Cam 10.1 Passage 2",
    url: "https://study4.com/tests/1182/trends-and-prospects-for-european-transport-systems/solutions/",
    type: "study4"
  },
  {
    unit: "unit-9",
    id: "ai-attitudes",
    title: "Attitudes towards Artificial Intelligence",
    cambridgeRef: "Cam 16.4 Passage 3",
    url: "https://ieltstrainingonline.com/practice-cam-16-reading-test-04-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 3
  },
  {
    unit: "unit-9",
    id: "future-of-work",
    title: "The Future of Work",
    cambridgeRef: "Cam 16.1 Passage 3",
    url: "https://ieltstrainingonline.com/practice-cam-16-reading-test-01-with-answer/",
    type: "ieltstrainingonline",
    passageNum: 3
  }
];

function cleanParagraphs(text) {
  if (!text) return [];
  // Split on double newlines
  const rawParas = text.split(/\n\s*\n|\r\n\s*\r\n/);
  const clean = [];
  for (let p of rawParas) {
    p = p.replace(/\s+/g, ' ').trim();
    // Filter out common junk lines
    if (p.length < 20 && !/^[A-Z]\b/.test(p)) continue;
    if (/^(Đáp án|transcript|Thoát|Từ điển|Anh-Việt|Thesaurus|Tiếng Trung|Passage \d)/i.test(p)) continue;
    if (/^(Đăng ký|Đăng nhập|Thông báo|Lịch học|Trang cá nhân|Bản quyền)/i.test(p)) continue;
    if (/^You should spend about 20 minutes/i.test(p)) continue;
    clean.push(p);
  }
  return clean;
}

async function scrapeStudy4(page, target) {
  await page.goto(target.url, { waitUntil: 'domcontentloaded', timeout: 20000 });

  const result = await page.evaluate((target) => {
    // Remove UI chrome
    document.querySelectorAll('.jqdictionary-wrapper, .site-elevator, header, footer, nav, .ad, .adsbygoogle').forEach(e => e.remove());

    let passageText = "";
    if (target.type === 'study4-oxytocin') {
      // Oxytocin is Passage 3 in test 2021
      const p3 = document.querySelector('#passage-3, [id*="passage-3"], .tab-pane:nth-child(3)');
      if (p3) {
        passageText = p3.innerText;
      } else {
        const full = document.body.innerText;
        const start = full.indexOf('Neurologists tend to divide');
        const end = full.indexOf('27\nĐáp án đúng');
        if (start !== -1 && end !== -1) {
          passageText = full.substring(start, end);
        }
      }
    } else {
      const pEl = document.querySelector('.passage-content, .reading-passage, .test-reading-passage');
      passageText = pEl ? pEl.innerText : document.body.innerText;
    }

    // Answers
    const answers = [];
    document.querySelectorAll('.question-wrapper').forEach(q => {
      const t = q.innerText.trim();
      if (t) {
        answers.push(t.replace(/\s+/g, ' '));
      }
    });

    return {
      rawText: passageText,
      answers
    };
  }, target);

  return {
    paragraphs: cleanParagraphs(result.rawText),
    answers: result.answers
  };
}

async function scrapeIeltsTrainingOnline(page, target) {
  await page.goto(target.url, { waitUntil: 'domcontentloaded', timeout: 20000 });

  const fullText = await page.evaluate(() => {
    document.querySelectorAll('header, footer, nav, aside, .adsbygoogle, .sidebar, script, style').forEach(e => e.remove());
    const entry = document.querySelector('.entry-content') || document.body;
    return entry.innerText;
  });

  const pNum = target.passageNum;
  // Match "READING PASSAGE 1", "READING PASSAGE 2", etc.
  const regexCurrent = new RegExp(`READING\\s+PASSAGE\\s+${pNum}\\b`, 'i');
  const regexNext = new RegExp(`READING\\s+PASSAGE\\s+${pNum + 1}\\b|Cambridge\\s+IELTS\\s+\\d+\\s+Reading\\s+Test\\s+\\d+\\s+Answers|Answer\\s+Key`, 'i');

  const matchCurrent = fullText.search(regexCurrent);
  if (matchCurrent === -1) {
    return { paragraphs: cleanParagraphs(fullText), questions: [] };
  }

  const afterCurrent = fullText.substring(matchCurrent);
  const matchNext = afterCurrent.search(regexNext);
  const sectionText = matchNext !== -1 ? afterCurrent.substring(0, matchNext) : afterCurrent;

  // Split into Passage text and Questions text at first "Questions X-Y"
  const qMatch = sectionText.search(/\n\s*Questions?\s+\d+[\s–-]+\d+/i);
  let passagePart = sectionText;
  let questionsPart = "";
  if (qMatch !== -1) {
    passagePart = sectionText.substring(0, qMatch);
    questionsPart = sectionText.substring(qMatch);
  }

  return {
    paragraphs: cleanParagraphs(passagePart),
    questions: cleanParagraphs(questionsPart)
  };
}

async function scrapeIeltsDeal(page, target) {
  await page.goto(target.url, { waitUntil: 'domcontentloaded', timeout: 20000 });

  const result = await page.evaluate(() => {
    document.querySelectorAll('header, footer, nav, aside, .adsbygoogle').forEach(e => e.remove());
    const entry = document.querySelector('.entry-content') || document.body;
    return entry.innerText;
  });

  // Cut before solutions
  let relevant = result;
  const solIdx = relevant.search(/\n(Solutions?|Answers?|Detailed Explanations?):/i);
  if (solIdx !== -1) {
    relevant = relevant.substring(0, solIdx);
  }

  const qIdx = relevant.search(/\nQuestions?\s+\d+/i);
  let passage = relevant;
  let questions = "";
  if (qIdx !== -1) {
    passage = relevant.substring(0, qIdx);
    questions = relevant.substring(qIdx);
  }

  return {
    paragraphs: cleanParagraphs(passage),
    questions: cleanParagraphs(questions)
  };
}

async function main() {
  console.log("Launching Brave browser via Playwright...");
  const browser = await chromium.launch({
    executablePath: 'C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe',
    headless: true
  });

  const context = await browser.newContext();
  await context.addCookies(COOKIES);

  const consolidated = {};
  let totalSuccess = 0;

  for (let i = 0; i < READING_TARGETS.length; i++) {
    const item = READING_TARGETS[i];
    console.log(`\n[${i + 1}/${READING_TARGETS.length}] Fetching ${item.title} (${item.cambridgeRef})...`);
    
    const page = await context.newPage();
    try {
      let data = null;
      if (item.type.startsWith('study4')) {
        data = await scrapeStudy4(page, item);
      } else if (item.type === 'ieltstrainingonline') {
        data = await scrapeIeltsTrainingOnline(page, item);
      } else if (item.type === 'ieltsdeal') {
        data = await scrapeIeltsDeal(page, item);
      }

      if (!consolidated[item.unit]) {
        consolidated[item.unit] = [];
      }

      const entry = {
        id: item.id,
        title: item.title,
        cambridgeRef: item.cambridgeRef,
        source: item.url,
        paragraphCount: data.paragraphs.length,
        paragraphs: data.paragraphs,
        questions: data.questions || [],
        answers: data.answers || []
      };

      consolidated[item.unit].push(entry);
      console.log(`  [OK] Parsed ${data.paragraphs.length} clean paragraphs, ${(data.questions || data.answers || []).length} items`);
      totalSuccess++;
    } catch (err) {
      console.error(`  [ERROR] Failed to extract ${item.id}:`, err.message);
    } finally {
      await page.close();
    }
  }

  await browser.close();

  // Write ONE single consolidated file
  fs.writeFileSync(OUTPUT_FILE, JSON.stringify(consolidated, null, 2), 'utf-8');
  console.log("\n" + "=".repeat(60));
  console.log(`Extraction complete! Saved ${totalSuccess} clean passages to:`);
  console.log(OUTPUT_FILE);
  console.log("=".repeat(60));
}

main().catch(console.error);
