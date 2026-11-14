const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: 'new',
  });
  const page = await browser.newPage();
  await page.goto('http://localhost:8099/index.html', { waitUntil: 'domcontentloaded' });
  const r = await page.evaluate(async () => {
    const { dotnet } = await import('./_framework/dotnet.js');
    const { getAssemblyExports, getConfig } = await dotnet.create();
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    const dump = (o, d) => d > 3 ? '...' : Object.fromEntries(Object.entries(o).map(([k, v]) => [k, typeof v === 'object' && v ? dump(v, d + 1) : typeof v]));
    return JSON.stringify(dump(exports, 0)).slice(0, 800);
  });
  console.log(r);
  await browser.close();
})();
