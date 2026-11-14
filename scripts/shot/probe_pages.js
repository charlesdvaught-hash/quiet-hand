const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push(e.message.slice(0, 250)));
  page.on('console', m => { if (m.type() === 'error' && !m.text().includes('404')) errs.push('C:' + m.text().slice(0, 150)); });
  await page.setViewport({ width: 1280, height: 800 });
  await page.goto('https://charlesdvaught-hash.github.io/quiet-hand/', { waitUntil: 'domcontentloaded' });
  let bridge = false;
  for (let i = 0; i < 12 && !bridge; i++) {
    await new Promise(r => setTimeout(r, 5000));
    bridge = await page.evaluate(() => !!window.QH_BRIDGE);
    console.log(`t+${(i+1)*5}s bridge=${bridge}`);
  }
  if (!bridge) { console.log('errors:', errs); await page.screenshot({ path: 'scripts/shot/pages_live.png' }); await browser.close(); return; }
  const picked = await page.evaluate(() => {
    const c = [...document.querySelectorAll('[data-oid]')].find(x => x.dataset.oid === 'malware_worm');
    if (!c) return 'no card — cards=' + document.querySelectorAll('[data-oid]').length;
    c.click(); return 'picked';
  });
  await new Promise(r => setTimeout(r, 2500));
  const snap = await page.evaluate(() => window.QH_BRIDGE.api('/api/state'));
  const tick = await page.evaluate(async () => (await window.QH_BRIDGE.api('/api/tick?days=5', { method: 'POST' })).state?.day);
  console.log(JSON.stringify({ picked, day: snap?.day, compute: snap?.compute, tickedTo: tick, errors: errs }));
  await page.screenshot({ path: 'scripts/shot/pages_live.png' });
  await browser.close();
})();
