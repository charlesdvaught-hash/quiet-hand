const puppeteer = require('puppeteer-core');
(async () => {
  const b = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const p = await b.newPage();
  await p.setViewport({ width: 1400, height: 1000 });
  p.on('dialog', async d => { await d.accept(); });
  await p.goto('http://localhost:5087/', { waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 3000));
  await p.evaluate(() => openModal('evolve'));
  await new Promise(r => setTimeout(r, 800));
  await p.screenshot({ path: 'web.png' });
  const stats = await p.evaluate(() => ({
    nodes: document.querySelectorAll('.evo-node').length,
    lit: document.querySelectorAll('.evo-node[data-ev]').length,
  }));
  console.log(JSON.stringify(stats));
  await b.close();
})();
