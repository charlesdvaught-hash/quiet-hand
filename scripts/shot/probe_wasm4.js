const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const fails = [];
  page.on('response', r => { if (r.status() >= 400) fails.push(r.status() + ' ' + r.url()); });
  page.on('pageerror', e => fails.push('PAGEERROR: ' + e.message.slice(0, 300)));
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 15000));
  const bridge = await page.evaluate(() => !!window.QH_BRIDGE);
  console.log('bridge:', bridge);
  console.log(fails.slice(0, 20).join('\n'));
  await browser.close();
})();
