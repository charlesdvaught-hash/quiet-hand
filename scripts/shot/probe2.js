const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 800 });
  const reqs = [];
  page.on('request', r => { if (r.url().includes('openstreetmap')) reqs.push(r.url()); });
  await page.goto('http://localhost:5087', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 3000));

  // street zoom on NorCal (unit mercator: lon -122 -> x=-0.339, lat 37.4 -> y~-0.232)
  await page.evaluate(() => {
    const k = 5500000;
    const x = -0.3385, y = -0.2318; // norcal unit-space
    svg.call(zoomBehavior.transform, d3.zoomIdentity.translate(500 - x * k, 250 - y * k).scale(k));
  });
  await new Promise(r => setTimeout(r, 5000));
  await page.screenshot({ path: 'probe_street.png' });
  const info = await page.evaluate(() => ({
    k: window.__zoomK,
    imgs: [...document.querySelectorAll('#tiles-layer image')].map(i => ({
      h: i.getAttribute('xlink:href'), x: i.getAttribute('x'), y: i.getAttribute('y'),
      complete: i.getBBox ? 'svg' : '?',
    })),
  }));
  console.log(JSON.stringify(info, null, 1));
  console.log('tile reqs:', reqs.length, reqs.slice(0, 4));
  await browser.close();
})();
