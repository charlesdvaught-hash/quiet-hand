const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push(e.message.slice(0, 200)));
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 14000));

  // dismiss tutorial, pick worm if on origin screen
  await page.evaluate(() => {
    const c = [...document.querySelectorAll('[data-oid]')].find(x => x.dataset.oid === 'malware_worm');
    if (c) c.click();
  });
  await new Promise(r => setTimeout(r, 4000));
  // dismiss signal card
  await page.evaluate(() => {
    const s = [...document.querySelectorAll('*')].find(e => e.innerText === 'tap to dismiss');
    if (s) s.closest('div').click();
  });
  await new Promise(r => setTimeout(r, 1000));

  // reset to world view, then check where the world sits
  await page.evaluate(() => window.resetZoom && resetZoom());
  await new Promise(r => setTimeout(r, 1500));
  const m = await page.evaluate(() => {
    const g = document.querySelector('#map-zoom-layer');
    return { k: window.__zoomK, t: g ? g.getAttribute('transform') : null };
  });
  console.log('WORLD VIEW:', JSON.stringify(m));
  await page.screenshot({ path: 'scripts/shot/mobile_worldview.png' });

  // drag hard left+down 3 times — can the world leave the screen?
  for (let i = 0; i < 3; i++) {
    await page.touchscreen.touchStart(195, 400);
    await page.touchscreen.touchMove(380, 100);
    await page.touchscreen.touchEnd();
    await new Promise(r => setTimeout(r, 400));
  }
  const m2 = await page.evaluate(() => {
    const g = document.querySelector('#map-zoom-layer');
    return { k: window.__zoomK, t: g ? g.getAttribute('transform') : null };
  });
  console.log('AFTER FLING:', JSON.stringify(m2));
  await page.screenshot({ path: 'scripts/shot/mobile_fling.png' });

  console.log('ERRORS:', JSON.stringify(errs));
  await browser.close();
})().catch(e => { console.log('FAIL', e.message); process.exit(1); });
