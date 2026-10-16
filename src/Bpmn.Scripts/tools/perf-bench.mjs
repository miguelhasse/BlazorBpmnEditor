// Browser-level benchmark for the demo editor: first render, pan and zoom frame times, SVG node count.
//
//   dotnet run --project playground/Bpmn.Demo --urls http://localhost:5410   (Development environment)
//   node tools/perf-bench.mjs http://localhost:5410 [fixture.bpmn]
//
// Needs `playwright` resolvable from this folder (npm i --no-save playwright). Compare the output
// against the budget in docs/performance.md.
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://localhost:5403';
const fixture = process.argv[3] ?? 'large-virtualization-stress.bpmn';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });

await page.goto(`${base}/editor`, { waitUntil: 'networkidle' });
await page.waitForSelector('.joint-element', { timeout: 30000 });

const started = Date.now();
await page.locator('[role=combobox]').first().click();
await page.getByText(fixture).first().click();
await page.waitForFunction(() => document.querySelectorAll('.joint-element').length > 50, { timeout: 30000 });
const firstRenderMs = Date.now() - started;

// Frame deltas sampled while `gesture` runs.
const sampleFrames = async (gesture) => {
  await page.evaluate(() => {
    window.__frames = [];
    let last = performance.now();
    const tick = (now) => {
      window.__frames.push(now - last);
      last = now;
      if (window.__sampling) requestAnimationFrame(tick);
    };
    window.__sampling = true;
    requestAnimationFrame(tick);
  });
  await gesture();
  return page.evaluate(() => {
    window.__sampling = false;
    const sorted = [...window.__frames].sort((a, b) => a - b);
    const at = (q) => sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * q))];
    return { frames: sorted.length, median: +at(0.5).toFixed(1), p95: +at(0.95).toFixed(1) };
  });
};

const pan = await sampleFrames(async () => {
  await page.mouse.move(700, 450);
  await page.mouse.down();
  for (let i = 0; i < 60; i++) await page.mouse.move(700 - i * 8, 450 - i * 4);
  await page.mouse.up();
});

const zoom = await sampleFrames(async () => {
  await page.mouse.move(700, 450);
  for (let i = 0; i < 20; i++) {
    await page.mouse.wheel(0, i % 2 === 0 ? -120 : 120);
    await page.waitForTimeout(30);
  }
});

const nodes = await page.evaluate(() => document.querySelectorAll('svg *').length);
console.log(JSON.stringify({ fixture, firstRenderMs, pan, zoom, svgNodes: nodes, errors }, null, 2));
await browser.close();
