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
  await page.screenshot({ path: 'p4_raw.png' });

  // strip country fills entirely — do tiles show?
  await page.evaluate(() => {
    document.querySelectorAll('.country').forEach(c => { c.style.fillOpacity = 0; c.style.strokeWidth = 0.3; });
    document.querySelectorAll('.graticule').forEach(c => c.style.display = 'none');
  });
  await new Promise(r => setTimeout(r, 800));
  await page.screenshot({ path: 'p4_nofills.png' });
  await browser.close();
  console.log('done');
})();
