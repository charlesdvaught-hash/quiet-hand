const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push(e.message.slice(0, 200)));
  page.on('console', m => { if (m.type() === 'error' && !m.text().includes('404')) errs.push('CONSOLE: ' + m.text().slice(0, 200)); });

  // phone viewport — iPhone 14-ish
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  await new Promise(r => setTimeout(r, 14000));

  // layout check — does the shell fill the viewport? where's the dead space?
  const layout = await page.evaluate(() => {
    const shell = document.getElementById('app-shell') || document.body;
    const r = shell.getBoundingClientRect();
    const b = document.body.getBoundingClientRect();
    const zoom = getComputedStyle(document.body).zoom;
    return { vw: innerWidth, vh: innerHeight, shellW: Math.round(r.width), shellH: Math.round(r.height),
      shellX: Math.round(r.x), shellY: Math.round(r.y), bodyW: Math.round(b.width), bodyH: Math.round(b.height), zoom };
  });
  console.log('LAYOUT:', JSON.stringify(layout));

  // pick worm origin
  const picked = await page.evaluate(() => {
    const cards = [...document.querySelectorAll('[data-oid]')];
    const c = cards.find(x => x.dataset.oid === 'malware_worm') || cards[0];
    if (!c) return 'NO CARDS';
    c.click(); return 'picked ' + (c.dataset.oid || '?');
  });
  await new Promise(r => setTimeout(r, 4000));
  console.log('PICK:', picked);

  // atlas dots on the map?
  const dots = await page.evaluate(() => {
    const els = document.querySelectorAll('.atlas-dot');
    const ks = document.querySelectorAll('.keystone-group');
    const svg = document.getElementById('world-svg');
    const gr = svg ? svg.querySelector('#keystones-group') : null;
    return { atlasDots: els.length, keystoneGroups: ks.length, groupExists: !!gr };
  });
  console.log('DOTS:', JSON.stringify(dots));

  // map transform sanity — is the world visible on screen, not flung off?
  const tform = await page.evaluate(() => {
    const g = document.querySelector('#world-svg #map-root, #world-svg g.world');
    const m = document.querySelector('#world-svg');
    return { mapChildren: m ? m.querySelectorAll('g').length : 0,
      zoomK: window.__zoomK,
      transform: g ? g.getAttribute('transform') : 'no #map-root' };
  });
  console.log('MAP:', JSON.stringify(tform));

  await page.screenshot({ path: 'scripts/shot/mobile_world.png' });

  // click an atlas dot → inspector
  const click = await page.evaluate(() => {
    const g = document.querySelector('.atlas-dot')?.closest('g');
    if (!g) return 'no dot group';
    g.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    const insp = document.getElementById('inspector') || document.querySelector('[id*=inspect]');
    return { clicked: true, inspText: insp ? insp.innerText.slice(0, 300) : 'no inspector found' };
  });
  console.log('CLICK:', JSON.stringify(click));
  await page.screenshot({ path: 'scripts/shot/mobile_inspector.png' });

  console.log('ERRORS:', JSON.stringify(errs));
  await browser.close();
})().catch(e => { console.log('FAIL', e.message); process.exit(1); });
