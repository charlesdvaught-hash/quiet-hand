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
  await page.goto('https://charlesdvaught-hash.github.io/quiet-hand/', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 16000));

  const layout = await page.evaluate(() => {
    const r = (document.getElementById('app-shell') || document.body).getBoundingClientRect();
    return { vw: innerWidth, vh: innerHeight, shellW: Math.round(r.width), shellH: Math.round(r.height),
      x: Math.round(r.x), y: Math.round(r.y), zoom: getComputedStyle(document.body).zoom };
  });
  console.log('LAYOUT:', JSON.stringify(layout));

  await page.evaluate(() => {
    const c = [...document.querySelectorAll('[data-oid]')].find(x => x.dataset.oid === 'malware_worm');
    if (c) c.click();
  });
  await new Promise(r => setTimeout(r, 4000));

  const dots = await page.evaluate(() => ({
    atlasDots: document.querySelectorAll('.atlas-dot').length,
    zoomK: window.__zoomK,
  }));
  console.log('DOTS:', JSON.stringify(dots));
  await page.evaluate(() => window.resetZoom && resetZoom());
  await new Promise(r => setTimeout(r, 1500));
  await page.screenshot({ path: 'scripts/shot/live_mobile.png' });
  console.log('ERRORS:', JSON.stringify(errs));
  await browser.close();
})().catch(e => { console.log('FAIL', e.message); process.exit(1); });
