// Captures every Custom Pages documentation screenshot at 2x into docs/screenshots.
// Run python3 -I server.py 8811 in the background, then node shoot.mjs [file name ...] to take all or only the named ones.
// PLAYWRIGHT_CORE points at playwright-core's index.mjs and CHROMIUM at the Chromium binary.
import { existsSync } from 'node:fs';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';

const CORE = process.env.PLAYWRIGHT_CORE || 'playwright-core';
let chromium;
try {
  ({ chromium } = await import(CORE));
} catch (e) {
  console.error(`Cannot load playwright-core from "${CORE}". Set PLAYWRIGHT_CORE to playwright-core's index.mjs.`);
  process.exit(1);
}
const CHROMIUM = process.env.CHROMIUM || `${homedir()}/Library/Caches/ms-playwright/chromium-1148/chrome-mac/Chromium.app/Contents/MacOS/Chromium`;
if (!existsSync(CHROMIUM)) {
  console.error(`No Chromium at ${CHROMIUM}. Set CHROMIUM to a Chromium binary.`);
  process.exit(1);
}
const b = await chromium.launch({ executablePath: CHROMIUM });
const OUT = fileURLToPath(new URL('../../docs/screenshots/', import.meta.url));
const ROOT = 'http://127.0.0.1:8811/';
const PAD = 12;

// Dashboard tabs: a 720 px window, clipped to the 714 px content area from top to bottom.
const tabs = [
  { file: 'Pages', page: 'custompages_pages', idx: 0, top: 'form', bottom: '.base-selector' },
  { file: 'Page Editor', page: 'custompages_pages', idx: 0, top: '#pageEditor', bottom: '#pageSaveBar' },
  { file: 'Visibility', page: 'custompages_pages', idx: 0, top: '#pageEditor', bottom: '#accessRow' },
  { file: 'System Access', page: 'custompages_pages', idx: 1, top: '#pageUnsandboxed', up: '.checkboxContainer', bottom: '#routesRow' },
  { file: 'Restricted Page', page: 'custompages_pages', idx: 2, top: '#pageVisibility', up: '.selectContainer', bottom: '#allowedUsersRow' },
  { file: 'Assets', page: 'custompages_assets', top: 'form', bottom: '#assetList' },
  { file: 'Stores', page: 'custompages_stores', top: 'form', bottom: '#storeSaveBar' }
];
// Served pages: a 960 px window, whole, through the real sign in shell.
const served = [
  { file: 'House Rules Page', slug: 'house-rules', h: 760 },
  { file: 'Requests Page', slug: 'requests', h: 680 },
  { file: 'Not Authorized', slug: 'members-only', h: 480 }
];
const only = process.argv.slice(2);
const wanted = (s) => !only.length || only.includes(s.file);

for (const s of tabs.filter(wanted)) {
  const p = await b.newPage({ viewport: { width: 720, height: 900 }, deviceScaleFactor: 2 });
  await p.goto(`${ROOT}harness.html?page=${s.page}`);
  await p.waitForFunction(() => window.__ready); await p.waitForTimeout(800);
  if (s.idx !== undefined) await p.evaluate((i) => { const e = document.querySelector('#selectPage'); e.value = String(i); e.dispatchEvent(new Event('change', { bubbles: true })); }, s.idx);
  await p.evaluate(() => document.activeElement && document.activeElement.blur());
  await p.waitForTimeout(600);
  const r = await p.evaluate((s) => {
    let t = document.querySelector(s.top); if (t && s.up) t = t.closest(s.up);
    const bt = document.querySelector(s.bottom);
    if (!t || !bt) return null;
    return { top: t.getBoundingClientRect().top + scrollY, bottom: bt.getBoundingClientRect().bottom + scrollY, errs: window.__errors };
  }, s);
  if (!r) { console.log('missing', s.file); await p.close(); continue; }
  const top = Math.max(0, Math.round(r.top - PAD));
  const clip = { x: 3, y: top, width: 714, height: Math.round(r.bottom + PAD) - top };
  await p.screenshot({ path: `${OUT}${s.file}.png`, clip, fullPage: true });
  console.log(s.file, clip.width * 2 + 'x' + clip.height * 2, JSON.stringify(r.errs));
  await p.close();
}

for (const s of served.filter(wanted)) {
  const ctx = await b.newContext({ viewport: { width: 960, height: s.h }, deviceScaleFactor: 2 });
  // A sample signed in client entry for the kit's server, so the shell takes its signed in path.
  await ctx.addInitScript(() => localStorage.setItem('jellyfin_credentials', JSON.stringify({ Servers: [{ Id: 's1', AccessToken: 'sample-token', ManualAddress: location.origin }] })));
  const p = await ctx.newPage();
  const errs = []; p.on('pageerror', (e) => errs.push(e.message));
  await p.goto(`${ROOT}pages/${s.slug}`);
  await p.waitForTimeout(1500);
  await p.screenshot({ path: `${OUT}${s.file}.png` });
  console.log(s.file, '1920x' + s.h * 2, JSON.stringify(errs));
  await ctx.close();
}
await b.close();
