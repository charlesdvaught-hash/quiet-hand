const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 12000));
  const res = await page.evaluate(() => {
    const cands = {
      turkey: [[29.0,41.2],[29.2,41.1],[29.4,41.1],[29.6,41.0],[29.0,41.3],[28.9,41.3],[29.9,40.9],[30.0,41.0],[28.6,41.2],[27.9,41.0],[30.5,41.2],[29.15,41.2]],
      makassar: [[119.46,-5.16],[119.5,-5.2],[119.6,-5.25],[119.45,-5.3],[119.55,-5.3],[119.5,-5.4],[119.7,-5.3],[119.4,-5.4],[119.65,-5.2],[119.42,-5.2]]
    };
    const out = {};
    for (const [k, arr] of Object.entries(cands))
      out[k] = arr.filter(c => topoFeatures.some(f => d3.geoContains(f, c)));
    return out;
  });
  console.log(JSON.stringify(res));
  await browser.close();
})().catch(e => { console.log('FAIL', e.message); process.exit(1); });
