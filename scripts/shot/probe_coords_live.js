const puppeteer = require('puppeteer-core');
(async () => {
  const b = await puppeteer.launch({ executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: 'new' });
  const p = await b.newPage();
  await p.setViewport({ width: 390, height: 844 });
  const errs = [];
  p.on('pageerror', e => errs.push(String(e)));
  await p.goto('https://charlesdvaught-hash.github.io/quiet-hand/', { waitUntil: 'domcontentloaded', timeout: 60000 });
  await new Promise(r => setTimeout(r, 14000));
  await p.evaluate(() => {
    const c = [...document.querySelectorAll('[data-oid]')].find(x => x.dataset.oid === 'malware_worm');
    if (c) c.click();
  });
  await new Promise(r => setTimeout(r, 5000));
  const r = await p.evaluate(() => {
    const k = (typeof state !== "undefined" ? state.keystones : []) || [];
    const land = f => window.topoFeatures ? topoFeatures.some(ft => d3.geoContains(ft, f)) : null;
    const wet = k.filter(x => x.coords && land(x.coords) === false).map(x => x.id + '@' + x.coords.map(n=>n.toFixed(2)));
    const get = id => { const x = k.find(y => y.id === id); return x && x.coords; };
    return { total: k.length, wetCount: wet.length, wet: wet.slice(0,25),
      eos: get('nvidia_eos_selene'), frontier: get('frontier_ornl'),
      fugaku: get('fugaku_riken'), lumi: get('lumi_csc'), jupiter: get('jupiter_julich') };
  });
  console.log('COORDS:', JSON.stringify(r));
  console.log('ERRORS:', JSON.stringify(errs));
  await b.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
