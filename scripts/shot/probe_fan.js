const puppeteer = require('puppeteer-core');
(async () => {
  const b = await puppeteer.launch({ executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: 'new' });
  const p = await b.newPage();
  await p.setViewport({ width: 390, height: 844 });
  const errs = [];
  p.on('pageerror', e => errs.push(String(e)));
  await p.goto(process.env.URL || 'http://127.0.0.1:8619/index.html', { waitUntil: 'domcontentloaded', timeout: 60000 });
  await new Promise(r => setTimeout(r, 14000));
  await p.evaluate(() => {
    const c = [...document.querySelectorAll('[data-oid]')].find(x => x.dataset.oid === 'malware_worm');
    if (c) c.click();
  });
  await new Promise(r => setTimeout(r, 5000));
  const r = await p.evaluate(() => {
    const k = (typeof state !== 'undefined' ? state.keystones : []);
    const land = f => topoFeatures.some(ft => d3.geoContains(ft, f));
    const wet = k.filter(x => x.coords && !land(x.coords)).map(x => x.id);
    // LLNL trio should be fanned apart
    const llnl = ['el_capitan_llnl','sierra_llnl','tuolumne_llnl'].map(id => k.find(x => x.id === id).coords);
    const dist = (a,b) => Math.hypot(a[0]-b[0], a[1]-b[1]).toFixed(3);
    return { total: k.length, wetCount: wet.length, wet: wet.slice(0,10),
      llnl, llnlSpread: [dist(llnl[0],llnl[1]), dist(llnl[1],llnl[2]), dist(llnl[0],llnl[2])] };
  });
  console.log('FAN:', JSON.stringify(r));
  console.log('ERRORS:', JSON.stringify(errs));
  await b.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
