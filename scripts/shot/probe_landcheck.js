const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push(e.message.slice(0, 200)));
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 14000));
  await page.evaluate(() => {
    const c = [...document.querySelectorAll('[data-oid]')].find(x => x.dataset.oid === 'malware_worm');
    if (c) c.click();
  });
  await new Promise(r => setTimeout(r, 4000));
  const res = await page.evaluate(() => {
    const wet = [];
    const dry = { k: 0, a: 0 };
    for (const k of (window._qh?.state?.keystones || [])) {
      const land = topoFeatures.some(f => d3.geoContains(f, k.coords));
      if (!land) wet.push(`${k.id} @ ${k.coords.map(v => v.toFixed(2))}`);
      else (k.ambient ? dry.a++ : dry.k++);
    }
    // page-lock checks
    const bodyFixed = getComputedStyle(document.body).position;
    const scrollable = document.documentElement.scrollHeight > innerHeight + 5
      || document.documentElement.scrollWidth > innerWidth + 5;
    return { wetCount: wet.length, wet: wet.slice(0, 8), dry, bodyFixed, scrollable,
      docH: document.documentElement.scrollHeight, docW: document.documentElement.scrollWidth };
  });
  console.log('LANDCHECK:', JSON.stringify(res, null, 1));
  console.log('ERRORS:', JSON.stringify(errs));
  await browser.close();
})().catch(e => { console.log('FAIL', e.message); process.exit(1); });
