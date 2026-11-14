const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 12000));
  const res = await page.evaluate(async () => {
    const geo = await (await fetch('ui_geo.json')).json();
    const wet = [];
    for (const [rid, g] of Object.entries(geo)) {
      if (!topoFeatures.some(f => d3.geoContains(f, g.pin))) wet.push(rid + ' @ ' + g.pin);
    }
    return wet;
  });
  console.log(JSON.stringify(res));
  await browser.close();
})().catch(e => { console.log('FAIL', e.message); process.exit(1); });
