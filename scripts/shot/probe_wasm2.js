const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const logs = [];
  page.on('console', m => logs.push(m.type() + ': ' + m.text().slice(0, 300)));
  page.on('pageerror', e => logs.push('PAGEERROR: ' + e.message.slice(0, 300)));
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  const r = await page.evaluate(async () => {
    try { await import('./main.js'); return 'imported, bridge=' + !!window.QH_BRIDGE; }
    catch (e) { return 'IMPORT FAILED: ' + (e.stack || e.message || String(e)).slice(0, 500); }
  });
  await new Promise(r => setTimeout(r, 12000));
  const bridge = await page.evaluate(() => !!window.QH_BRIDGE);
  console.log('import result:', r, '| bridge now:', bridge);
  console.log(logs.slice(-10).join('\n'));
  await browser.close();
})();
