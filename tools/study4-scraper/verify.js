const fs = require('fs');
const path = require('path');

const data = JSON.parse(fs.readFileSync(path.join(__dirname, 'consolidated_readings.json'), 'utf8'));

let totalPassages = 0;
let totalParagraphs = 0;
let totalItems = 0;

console.log("=== CONSOLIDATED READINGS SUMMARY ===");
for (const [unit, list] of Object.entries(data)) {
  console.log(`\n[${unit.toUpperCase()}] (${list.length} passages):`);
  for (const item of list) {
    totalPassages++;
    totalParagraphs += item.paragraphs.length;
    const itemCount = (item.questions && item.questions.length) || (item.answers && item.answers.length) || 0;
    totalItems += itemCount;
    console.log(`  * ${item.title} (${item.cambridgeRef}): ${item.paragraphs.length} paras, ${itemCount} items`);
  }
}

console.log("\n" + "=".repeat(50));
console.log(`TOTALS: ${totalPassages} passages, ${totalParagraphs} paragraphs, ${totalItems} question items.`);
console.log("=".repeat(50));

