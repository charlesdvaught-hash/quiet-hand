const puppeteer = require('puppeteer-core');

(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
    args: ['--window-size=1280,800', '--disable-gpu'],
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1280, height: 800 });

  const fails = [];
  page.on('requestfailed', r => fails.push(`FAIL ${r.url().slice(0, 90)} ${r.failure()?.errorText}`));
  page.on('response', r => { if (!r.ok()) fails.push(`HTTP${r.status()} ${r.url().slice(0, 90)}`); });
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') fails.push(`CONSOLE ${m.text().slice(0, 120)}`); });

  await page.goto('http://localhost:5087', { waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 2500));
  await page.screenshot({ path: 'shot_intro.png' });

  const picked = await page.evaluate(() => {
    const el = document.querySelector('#intro-overlay [data-oid]');
    if (el) { el.click(); return true; }
    return false;
  });
  console.log('origin picked:', picked);
  await new Promise(r => setTimeout(r, 6000)); // dive transition + tiles
  await page.screenshot({ path: 'shot_home_street.png' });

  const zoom = async (k, name) => {
    await page.evaluate(async (k) => {
      const st = await fetch('/api/state').then(r => r.json());
      const g = (await fetch('/ui_geo.json').then(r => r.json()))[st.homeRegion];
      const [x, y] = projection(g.pin);
      svg.call(zoomBehavior.transform, d3.zoomIdentity.translate(500 - x * k, 250 - y * k).scale(k));
    }, k);
    await new Promise(r => setTimeout(r, 3500));
    await page.screenshot({ path: `shot_${name}.png` });
  };

  await zoom(900, 'world');
  await zoom(200000, 'regional');
  await zoom(5500000, 'street');

  const diag = await page.evaluate(() => ({
    zoomK: window.__zoomK,
    tiles: document.querySelectorAll('#tiles-layer image').length,
    pinGroups: document.querySelectorAll('.keystone-group').length,
    tileHref: document.querySelector('#tiles-layer image')?.getAttribute('xlink:href'),
    mapT: document.getElementById('map-zoom-layer')?.getAttribute('transform')?.slice(0, 60),
    tilesT: document.getElementById('tiles-layer')?.getAttribute('transform')?.slice(0, 60),
  }));
  console.log('DIAG', JSON.stringify(diag));
  console.log('FAILURES:\n' + (fails.slice(0, 25).join('\n') || 'none'));
  await browser.close();
})();
