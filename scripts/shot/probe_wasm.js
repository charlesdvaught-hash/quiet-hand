const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const logs = [];
  page.on('console', m => logs.push(m.type() + ': ' + m.text().slice(0, 200)));
  page.on('pageerror', e => logs.push('PAGEERROR: ' + e.message.slice(0, 300)));
  await page.setViewport({ width: 1280, height: 800 });
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 15000));
  const info = await page.evaluate(() => ({
    bridge: !!window.QH_BRIDGE,
    overlay: !!document.querySelector('[id*=intro], [class*=intro], [id*=origin]'),
    bodyClasses: document.body.className,
  }));
  console.log(JSON.stringify(info));
  console.log(logs.slice(-12).join('\n'));
  await page.screenshot({ path: 'scripts/shot/wasm_boot.png' });
  await browser.close();
})();
