// Captures the Artwork Generator docs screenshots from the harness at 2x, dark theme, into docs/screenshots.
// Start the server first (dotnet run --project server), then node shoot.mjs [name ...] to take all or only the named ones.
// PLAYWRIGHT_CORE points at playwright-core's index.mjs and CHROMIUM at the Chromium binary.
import { existsSync } from 'node:fs';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';
let chromium;
try {
  ({ chromium } = await import(process.env.PLAYWRIGHT_CORE || 'playwright-core'));
} catch (e) {
  console.error(`playwright-core not found (${e.message}). Set PLAYWRIGHT_CORE to playwright-core's index.mjs.`);
  process.exit(1);
}
const CHROMIUM = process.env.CHROMIUM || `${homedir()}/Library/Caches/ms-playwright/chromium-1148/chrome-mac/Chromium.app/Contents/MacOS/Chromium`;
if (!existsSync(CHROMIUM)) {
  console.error(`Chromium not found at ${CHROMIUM}. Set CHROMIUM to a Chromium binary.`);
  process.exit(1);
}
const OUT = fileURLToPath(new URL('../../docs/screenshots/', import.meta.url));
const b = await chromium.launch({ executablePath: CHROMIUM });
const PAD = 12;
async function open(page) {
  const p = await b.newPage({ viewport: { width: 860, height: 1000 }, deviceScaleFactor: 2, colorScheme: 'dark' });
  await p.goto('http://127.0.0.1:8849/index.html?page=' + page);
  await p.waitForFunction(() => window.__ready); await p.waitForTimeout(1500);
  return p;
}
async function choose(p, sel, value) {
  await p.evaluate(([s, v]) => { const e = document.querySelector(s); e.value = v; e.dispatchEvent(new Event('change', { bubbles: true })); }, [sel, value]);
}
async function expand(p, target) { await p.click(`.base-collapsible-header[data-target="${target}"]`); await p.waitForTimeout(400); }
async function settle(p, ms = 2500) { await p.evaluate(() => document.activeElement && document.activeElement.blur()); await p.mouse.move(850, 5); await p.waitForTimeout(ms); }
async function shot(p, file, top, bottom) {
  const r = await p.evaluate(([t, bt]) => {
    const a = document.querySelector(t), z = document.querySelector(bt);
    const f = document.querySelector('form').getBoundingClientRect();
    return { top: a.getBoundingClientRect().top + scrollY, bottom: z.getBoundingClientRect().bottom + scrollY, left: f.left, width: f.width, log: window.__log.filter(l => !/^(GET|POST)/.test(l)) };
  }, [top, bottom]);
  const y = Math.max(0, Math.round(r.top - PAD));
  const clip = { x: Math.round(r.left - PAD), y, width: Math.round(r.width + 2 * PAD), height: Math.round(r.bottom + PAD) - y };
  await p.screenshot({ path: OUT + file, clip, fullPage: true });
  console.log(file, clip.width * 2 + 'x' + clip.height * 2, JSON.stringify(r.log));
}
const only = process.argv.slice(2);
const want = (n) => !only.length || only.includes(n);

if (want('Designs') || want('Preview-Modal')) {
  const p = await open('ag_posters');
  const id = await p.evaluate(() => [...document.querySelector('#selectPosterConfig').options].find(o => o.textContent === 'Bold Title').value);
  await choose(p, '#selectPosterConfig', id);
  await p.waitForTimeout(500);
  await choose(p, '#selectPreviewKind', 'Episode');
  await settle(p, 3500);
  if (want('Designs')) await shot(p, 'Designs.png', '.sectionTitleContainer', '#saveBarHost');
  if (want('Preview-Modal')) {
    await p.click('#posterPreviewLandscape img, #posterPreviewLandscape');
    await p.waitForTimeout(1500);
    const box = await p.evaluate(() => {
      const d = [...document.querySelectorAll('.base-dialog, [role="dialog"], dialog, .dialog')].filter(e => e.getBoundingClientRect().width > 0).pop();
      if (!d) return null; const r = d.getBoundingClientRect(); return { x: r.left, y: r.top, width: r.width, height: r.height, cls: d.className };
    });
    console.log('dialog', JSON.stringify(box));
    if (box) await p.screenshot({ path: OUT + 'Preview-Modal.png', clip: { x: box.x, y: box.y, width: box.width, height: box.height } });
  }
  await p.close();
}
if (want('Logos')) {
  const p = await open('ag_logos');
  await p.fill('#txtSampleName', 'Northern Lights');
  await settle(p, 3000);
  await shot(p, 'Logos.png', '.sectionTitleContainer', '#saveBarHost');
  await p.close();
}
if (want('Profiles')) {
  const p = await open('ag_profiles');
  await choose(p, '#selectProfile', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');
  await settle(p, 1500);
  await shot(p, 'Profiles.png', '.sectionTitleContainer', '#saveBarHost');
  await p.close();
}
if (want('Settings')) {
  const p = await open('ag_settings');
  await expand(p, 'frameExtractionContent');
  await settle(p, 800);
  await shot(p, 'Settings.png', '.sectionTitleContainer', '#saveBarHost');
  await p.close();
}
if (want('Fonts')) {
  const p = await open('ag_settings');
  await expand(p, 'fontsContent');
  await settle(p, 800);
  await shot(p, 'Fonts.png', '#AgFontsForm', '#fontUploadBarHost');
  await p.close();
}
await b.close();
