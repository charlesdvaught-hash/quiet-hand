const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 800 });
  await page.goto('http://localhost:5087', { waitUntil: 'networkidle2' });
  await new Promise(r => setTimeout(r, 3000));

  await page.evaluate(() => {
    const k = 5500000, [x, y] = projection([-122.0, 37.4]);
    svg.call(zoomBehavior.transform, d3.zoomIdentity.translate(500 - x * k, 250 - y * k).scale(k));
  });
  await new Promise(r => setTimeout(r, 4500));

  const info = await page.evaluate(() => {
    const tl = document.getElementById('tiles-layer');
    const img = tl.querySelector('image');
    const r = img?.getBoundingClientRect();
    const atCenter = document.elementFromPoint(430, 210);
    const svgEl = document.getElementById('world-svg');
    return {
      tilesT: tl.getAttribute('transform'),
      firstImgRect: r ? { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) } : null,
      firstImgHref: img?.getAttributeNS('http://www.w3.org/1999/xlink', 'href'),
      centerElement: atCenter ? `${atCenter.tagName}.${atCenter.getAttribute('class') || ''}` : 'none',
      svgRect: (() => { const b = svgEl.getBoundingClientRect(); return { x: b.x, y: b.y, w: b.width, h: b.height }; })(),
      svgBg: getComputedStyle(svgEl).background.slice(0, 120),
      imgComplete: img ? (img instanceof SVGImageElement ? 'svgimg' : typeof img) : null,
    };
  });
  console.log(JSON.stringify(info, null, 1));
  await page.screenshot({ path: 'p5.png' });
  await browser.close();
})();
