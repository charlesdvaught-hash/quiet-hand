const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push(e.message.slice(0, 200)));
  page.on('console', m => { if (m.type() === 'error' && !m.text().includes('404')) errs.push('CONSOLE: ' + m.text().slice(0, 200)); });
  await page.setViewport({ width: 1280, height: 800 });
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 12000));

  // pick the worm origin
  const picked = await page.evaluate(() => {
    const cards = [...document.querySelectorAll('[data-oid]')];
    const c = cards.find(x => x.dataset.oid === 'malware_worm') || cards[0];
    if (!c) return 'NO CARDS: ' + document.body.innerText.slice(0, 200);
    c.click(); return 'picked ' + (c.dataset.oid || '?');
  });
  await new Promise(r => setTimeout(r, 3000));
  const s1 = await page.evaluate(() => window.QH_BRIDGE ? JSON.parse(window._qhDebugState?.() || '{}') : null).catch(() => null);
  const snap = await page.evaluate(async () => {
    const b = window.QH_BRIDGE;
    return b ? await b.api('/api/state') : null;
  });
  console.log('pick:', picked, '| day:', snap?.day, 'compute:', snap?.compute, 'speed:', snap?.speed ?? snap?.simSpeed, '| nodes:', JSON.stringify(snap?.nodeCounts));

  // tick 10 days via the real loop path
  const t = await page.evaluate(async () => {
    const r = await window.QH_BRIDGE.api('/api/tick?days=10', { method: 'POST' });
    return { day: r.state?.day, compute: r.state?.compute, events: r.events?.length };
  });
  console.log('after tick:', JSON.stringify(t));

  // save round-trip: reload page, continue
  await page.reload({ waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 12000));
  const cont = await page.evaluate(async () => {
    const b = window.QH_BRIDGE;
    const save = await b.api('/api/save');
    const snap = await b.api('/api/state');
    return { saveExists: save.exists, day: snap?.day, compute: snap?.compute };
  });
  console.log('after reload:', JSON.stringify(cont));
  console.log('errors:', errs.length ? errs.join(' | ') : 'none');
  await page.screenshot({ path: 'scripts/shot/wasm_play.png' });
  await browser.close();
})();
