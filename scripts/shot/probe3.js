const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 800 });
  page.on('console', m => console.log('CON', m.text().slice(0, 140)));
  await page.goto('http://localhost:5087', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 3000));

  await page.evaluate(() => {
    const k = 5500000, x = -0.3385, y = -0.2318;
    svg.call(zoomBehavior.transform, d3.zoomIdentity.translate(500 - x * k, 250 - y * k).scale(k));
  });
  await new Promise(r => setTimeout(r, 5000));

  const info = await page.evaluate(() => {
    const imgs = [...document.querySelectorAll('#tiles-layer image')];
    return {
      count: imgs.length,
      first: imgs[0] ? {
        nsHref: imgs[0].getAttributeNS('http://www.w3.org/1999/xlink', 'href'),
        hrefBaseVal: imgs[0].href?.baseVal,
        outer: imgs[0].outerHTML.slice(0, 260),
      } : null,
      perf: performance.getEntriesByType('resource')
        .filter(e => e.name.includes('openstreetmap'))
        .map(e => e.name).slice(0, 10),
      tileLayerChildCount: document.getElementById('tiles-layer')?.children.length,
    };
  });
  console.log(JSON.stringify(info, null, 1));
  await browser.close();
})();
