const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 800 });
  const reqs = [];
  page.on('request', r => { if (r.url().includes('tile')) reqs.push(r.url()); });
  await page.goto('http://localhost:5087', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 4000));
  const out = await page.evaluate(() => {
    const imgs = [...document.querySelectorAll('#tiles-layer image')];
    return {
      count: imgs.length,
      html: imgs.slice(0, 3).map(i => i.outerHTML.slice(0, 300)),
      href: imgs[0]?.getAttribute('href'),
      xlink: imgs[0]?.getAttribute('xlink:href'),
      ns: imgs[0]?.getAttributeNS('http://www.w3.org/1999/xlink', 'href'),
      d3tile: typeof d3.tile,
      zoomK: window.__zoomK,
    };
  });
  console.log(JSON.stringify(out, null, 1));
  console.log('tile requests:', reqs.length, reqs.slice(0, 3));
  await browser.close();
})();
