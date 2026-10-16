// Browser-level benchmark for the demo editor: cold and warm start, switching to a large diagram, pan and zoom frame
// times, edit latency, long tasks, JS heap, transfer sizes and SVG node counts.
//
//   dotnet run --project playground/Bpmn.Demo -c Release --urls http://localhost:5410   (Development environment)
//   node tools/perf-bench.mjs http://localhost:5410 [--fixture=large-virtualization-stress.bpmn] [--runs=3]
//
// Needs `playwright-core` resolvable from this folder (npm i --no-save playwright-core) and a Chromium: either the one
// Playwright installed (set PLAYWRIGHT_CHROMIUM to its chrome executable if the versions differ) or the default.
// Every number is the median of --runs fresh browsers; the spread is reported too, because a single run of any of these
// has been wrong in both directions (see docs/performance.md). Output is JSON on stdout.
import { chromium } from 'playwright-core';

const args = process.argv.slice(2);
const option = (name, fallback) => args.find((a) => a.startsWith(`--${name}=`))?.split('=')[1] ?? fallback;
const base = args.find((a) => !a.startsWith('--')) ?? 'http://localhost:5403';
const fixture = option('fixture', 'large-virtualization-stress.bpmn');
const runs = Number(option('runs', '3'));

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)];
};

/** Folds the per-run objects into "median (min-max)" at every numeric leaf, so a noisy metric shows its spread. */
const summarize = (samples) => {
  const first = samples[0];
  if (typeof first === 'number') {
    const values = samples.map(Number);
    const fixed = (n) => +n.toFixed(1);
    return `${fixed(median(values))} (${fixed(Math.min(...values))}-${fixed(Math.max(...values))})`;
  }
  if (first && typeof first === 'object') {
    return Object.fromEntries(Object.keys(first).map((key) => [key, summarize(samples.map((s) => s[key]))]));
  }
  return first;
};

const launch = () => chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM || undefined });

async function newPage(browser) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  // Long tasks (>50 ms of main-thread work) are what a user feels as a hitch; collected for the whole page lifetime.
  await page.addInitScript(() => {
    window.__longTasks = [];
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) window.__longTasks.push(entry.duration);
    }).observe({ type: 'longtask', buffered: true });
  });
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });
  return { context, page, errors };
}

const longTasks = (page) => page.evaluate(() => {
  const tasks = window.__longTasks.splice(0);
  return { count: tasks.length, totalMs: +tasks.reduce((a, b) => a + b, 0).toFixed(0), maxMs: +Math.max(0, ...tasks).toFixed(0) };
});

const heapMb = async (page) => {
  const client = await page.context().newCDPSession(page);
  await client.send('Performance.enable');
  await client.send('HeapProfiler.collectGarbage');
  const { metrics } = await client.send('Performance.getMetrics');
  await client.detach();
  return +(metrics.find((m) => m.name === 'JSHeapUsedSize').value / 1048576).toFixed(1);
};

/** Bytes the page fetched, by kind: what a first visit costs on the wire (encoded) and in memory (decoded). */
const transfer = (page) => page.evaluate(() => {
  const kind = (e) => (/\.wasm/.test(e.name) ? 'wasm' : /\.(dll|pdb|dat|blat)/.test(e.name) ? 'assemblies' : /\.css/.test(e.name) ? 'css'
    : /\.m?js/.test(e.name) ? 'js' : 'other');
  const totals = {};
  for (const e of performance.getEntriesByType('resource')) {
    const t = (totals[kind(e)] ??= { requests: 0, kb: 0, decodedKb: 0 });
    t.requests += 1;
    t.kb += (e.encodedBodySize || 0) / 1024;
    t.decodedKb += (e.decodedBodySize || 0) / 1024;
  }
  for (const t of Object.values(totals)) { t.kb = Math.round(t.kb); t.decodedKb = Math.round(t.decodedKb); }
  return totals;
});

// Frame deltas sampled while `gesture` runs.
const sampleFrames = async (page, gesture) => {
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
  await longTasks(page);
  await gesture();
  const frames = await page.evaluate(() => {
    window.__sampling = false;
    const sorted = [...window.__frames].sort((a, b) => a - b);
    const at = (q) => sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * q))];
    return { frames: sorted.length, median: +at(0.5).toFixed(1), p95: +at(0.95).toFixed(1), max: +sorted[sorted.length - 1].toFixed(1) };
  });
  return { ...frames, longTasks: await longTasks(page) };
};

const joints = () => document.querySelectorAll('.joint-element').length;

/**
 * Delete the first element and undo it. Two numbers come out, because neither alone is the cost of an edit:
 * `treeMs` is the time until the accessible tree (one item per element) shows the change plus two animation frames, which comes
 * first and is quick; `blockedMs` is all the main-thread work (long tasks) in the 2.5 s that follow, which includes the canvas
 * rebuilding every cell from the whole document and is what the person actually waits through. `treeMs` alone has read 20 ms for
 * an edit that then froze the page for seconds.
 */
async function editRoundTrip(page) {
  const treeCount = () => document.querySelectorAll('.bpmn-a11y-tree__item').length;
  const before = await page.evaluate(treeCount);
  await page.locator('.joint-element').first().click();
  await longTasks(page);
  const settled = (predicate) => page.evaluate(([source, n]) => new Promise((resolve) => {
    const started = performance.now();
    const test = new Function('n', `return (${source})(n);`);
    const poll = () => (test(n) ? requestAnimationFrame(() => requestAnimationFrame(() => resolve(performance.now() - started))) : requestAnimationFrame(poll));
    poll();
  }), [predicate.toString(), before]);
  const treeMs = await Promise.all([settled((n) => document.querySelectorAll('.bpmn-a11y-tree__item').length < n), page.keyboard.press('Delete')]).then(([ms]) => ms);
  await page.waitForTimeout(2500);
  const deleteBlocked = await longTasks(page);
  const undoTreeMs = await Promise.all([settled((n) => document.querySelectorAll('.bpmn-a11y-tree__item').length >= n), page.keyboard.press('Control+z')]).then(([ms]) => ms);
  await page.waitForTimeout(2500);
  const undoBlocked = await longTasks(page);
  return {
    treeMs: +treeMs.toFixed(0),
    undoTreeMs: +undoTreeMs.toFixed(0),
    blockedMs: deleteBlocked.totalMs,
    longestMs: deleteBlocked.maxMs,
    undoBlockedMs: undoBlocked.totalMs,
    undoLongestMs: undoBlocked.maxMs,
  };
}

async function oneRun() {
  const browser = await launch();
  try {
    const { context, page, errors } = await newPage(browser);

    // Cold: a fresh context has an empty HTTP cache, so this is the first-visit cost, runtime download included.
    let started = performance.now();
    await page.goto(`${base}/editor`, { waitUntil: 'commit' });
    await page.waitForSelector('.joint-element', { timeout: 60000 });
    const coldMs = +(performance.now() - started).toFixed(0);
    await page.waitForLoadState('networkidle');
    const coldTransfer = await transfer(page);
    const coldLongTasks = await longTasks(page);

    // Warm: same context, HTTP cache populated; the runtime still starts again.
    started = performance.now();
    await page.reload({ waitUntil: 'commit' });
    await page.waitForSelector('.joint-element', { timeout: 60000 });
    const warmMs = +(performance.now() - started).toFixed(0);
    await page.waitForLoadState('networkidle');

    // What the previous harness called "first render": switching a running editor to the large diagram.
    await longTasks(page);
    const heapBefore = await heapMb(page);
    started = performance.now();
    await page.locator('[role=combobox]').first().click();
    await page.getByText(fixture).first().click();
    await page.waitForFunction(() => document.querySelectorAll('.joint-element').length > 50, undefined, { timeout: 60000, polling: 'raf' });
    const switchMs = +(performance.now() - started).toFixed(0);
    await page.waitForTimeout(500);
    const switchLongTasks = await longTasks(page);
    const heapAfterLoad = await heapMb(page);

    const nodesAtLoad = await page.evaluate(() => document.querySelectorAll('svg *').length);
    const cellsAtLoad = await page.evaluate(joints);
    const edit = await editRoundTrip(page);

    const pan = await sampleFrames(page, async () => {
      await page.mouse.move(700, 450);
      await page.mouse.down();
      for (let i = 0; i < 60; i++) await page.mouse.move(700 - i * 8, 450 - i * 4);
      await page.mouse.up();
    });

    // The earlier harness' zoom sample: plain wheel, which the default configuration treats as a pan, kept for comparability.
    const plainWheel = await sampleFrames(page, async () => {
      await page.mouse.move(700, 450);
      for (let i = 0; i < 20; i++) {
        await page.mouse.wheel(0, i % 2 === 0 ? -120 : 120);
        await page.waitForTimeout(30);
      }
    });

    // A real zoom: Ctrl+wheel, a sustained sweep in then all the way out again, so cells mount and unmount.
    let mountedMin = Infinity;
    let mountedMax = 0;
    const zoom = await sampleFrames(page, async () => {
      await page.mouse.move(700, 450);
      await page.keyboard.down('Control');
      for (const direction of [-1, 1]) {
        for (let i = 0; i < 40; i++) {
          await page.mouse.wheel(0, direction * 120);
          await page.waitForTimeout(16);
          const n = await page.evaluate(joints);
          mountedMin = Math.min(mountedMin, n);
          mountedMax = Math.max(mountedMax, n);
        }
      }
      await page.keyboard.up('Control');
      await page.waitForTimeout(300);
    });

    // The same edit with the whole diagram zoomed out, where every cell is mounted.
    const editZoomedOut = await editRoundTrip(page);

    const nodes = await page.evaluate(() => document.querySelectorAll('svg *').length);
    const heapEnd = await heapMb(page);
    await context.close();

    return {
      coldMs, warmMs, switchMs, edit, editZoomedOut,
      pan, plainWheel, zoom, mountedCells: { min: mountedMin, max: mountedMax },
      atLoad: { svgNodes: nodesAtLoad, mountedCells: cellsAtLoad },
      svgNodesAfterSweep: nodes,
      heapMb: { beforeLoad: heapBefore, afterLoad: heapAfterLoad, end: heapEnd },
      longTasks: { cold: coldLongTasks, switch: switchLongTasks },
      transfer: coldTransfer,
      errors: errors.length,
    };
  } finally {
    await browser.close();
  }
}

const results = [];
for (let i = 0; i < runs; i++) results.push(await oneRun());
console.log(JSON.stringify({ fixture, runs, ...summarize(results) }, null, 2));
