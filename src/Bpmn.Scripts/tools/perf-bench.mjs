// Browser-level benchmark for the demo editor: cold and warm start, switching to a large diagram, pan and zoom frame
// times, edit latency, long tasks, JS heap, transfer sizes and SVG node counts.
//
//   dotnet run --project playground/Bpmn.Demo -c Release --urls http://localhost:5410   (Development environment)
//   node tools/perf-bench.mjs http://localhost:5410 [--fixture=large-virtualization-stress.bpmn] [--shapes=602] [--runs=3]
//
// Needs `playwright-core` resolvable from this folder (npm i --no-save playwright-core) and a Chromium: either the one
// Playwright installed (set PLAYWRIGHT_CHROMIUM to its chrome executable if the versions differ) or the default.
// Every number is the median of --runs fresh browsers; the spread is reported too, because a single run of any of these
// has been wrong in both directions (see docs/performance.md). Output is JSON on stdout.
import { chromium } from 'playwright-core';
import { readFile, writeFile } from 'node:fs/promises';

const args = process.argv.slice(2);
const option = (name, fallback) => args.find((a) => a.startsWith(`--${name}=`))?.split('=')[1] ?? fallback;
const base = args.find((a) => !a.startsWith('--')) ?? 'http://localhost:5403';
const fixture = option('fixture', 'large-virtualization-stress.bpmn');
const fixtureFile = option('fixture-file', '');
const fixtureContent = fixtureFile ? await readFile(fixtureFile, 'utf8') : null;
const expectedShapes = Number(option('shapes', '602'));
const runs = Number(option('runs', '3'));
const profilePrefix = option('profile', '');
const isolatePan = args.includes('--isolate-pan');
const measureSelection = args.includes('--selection');
const measureSelectionControls = args.includes('--selection-controls');
if (measureSelectionControls && !measureSelection) throw new Error('--selection-controls requires --selection to warm the same workload first.');

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

async function settledCanvas(page) {
  await page.evaluate(() => new Promise((resolve, reject) => {
    const paper = document.querySelector('.bpmn-canvas__paper');
    if (!paper) { reject(new Error('Canvas paper is missing.')); return; }
    let quiet;
    const observer = new MutationObserver(() => schedule());
    const timeout = setTimeout(() => {
      observer.disconnect();
      clearTimeout(quiet);
      reject(new Error('Canvas did not settle within 30 seconds.'));
    }, 30000);
    const schedule = () => {
      clearTimeout(quiet);
      quiet = setTimeout(() => {
        observer.disconnect();
        clearTimeout(timeout);
        requestAnimationFrame(() => requestAnimationFrame(resolve));
      }, 100);
    };
    observer.observe(paper, { subtree: true, childList: true, attributes: true });
    schedule();
  }));
}

async function newPage(browser) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  if (fixtureContent !== null) {
    await page.route(`**/samples/${fixture}`, (route) =>
      route.fulfill({ contentType: 'application/xml', body: fixtureContent }));
  }
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
 * first and is quick; `blockedMs` is all the main-thread work (long tasks) until the canvas summary acknowledges the change and
 * the SVG settles. `treeMs` alone has read 20 ms for
 * an edit that then froze the page for seconds.
 */
async function editRoundTrip(page) {
  const treeCount = () => document.querySelectorAll('.bpmn-a11y-tree__item').length;
  const before = await page.evaluate(treeCount);
  const summaryBefore = await page.locator('.bpmn-canvas__summary').textContent();
  const summaryChanged = (previous) => page.waitForFunction((text) =>
    document.querySelector('.bpmn-canvas__summary')?.textContent !== text, previous, { timeout: 30000 });
  await page.locator('.joint-element').first().click();
  await longTasks(page);
  const settled = (predicate) => page.evaluate(([source, n]) => new Promise((resolve, reject) => {
    const started = performance.now();
    const test = new Function('n', `return (${source})(n);`);
    const poll = () => {
      if (performance.now() - started > 30000) {
        reject(new Error('Edit did not update the accessible tree within 30 seconds.'));
        return;
      }
      if (test(n)) requestAnimationFrame(() => requestAnimationFrame(() => resolve(performance.now() - started)));
      else requestAnimationFrame(poll);
    };
    poll();
  }), [predicate.toString(), before]);
  const treeMs = await Promise.all([settled((n) => document.querySelectorAll('.bpmn-a11y-tree__item').length < n), page.keyboard.press('Delete')]).then(([ms]) => ms);
  await summaryChanged(summaryBefore);
  await settledCanvas(page);
  const deleteBlocked = await longTasks(page);
  const summaryDeleted = await page.locator('.bpmn-canvas__summary').textContent();
  const undoTreeMs = await Promise.all([settled((n) => document.querySelectorAll('.bpmn-a11y-tree__item').length >= n), page.keyboard.press('Control+z')]).then(([ms]) => ms);
  await summaryChanged(summaryDeleted);
  await settledCanvas(page);
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

async function canvasLoadVersion(page) {
  return page.evaluate(() => {
    if (window.__bpmnSummaryVersion === undefined) {
      window.__bpmnSummaryVersion = 0;
      new MutationObserver(() => { window.__bpmnSummaryVersion++; })
        .observe(document.querySelector('.bpmn-canvas__summary'), { childList: true });
    }
    return window.__bpmnSummaryVersion;
  });
}

async function waitForCanvasLoad(page, version) {
  await page.waitForFunction((previous) => window.__bpmnSummaryVersion > previous, version, { timeout: 30000 });
  await settledCanvas(page);
}

async function renameRoundTrip(page, run) {
  await page.locator('.joint-element').first().dblclick();
  const input = page.locator('.bpmn-label-editor__input');
  await input.fill('Performance rename');
  const version = await canvasLoadVersion(page);
  await longTasks(page);
  const profiler = profilePrefix ? await page.context().newCDPSession(page) : null;
  if (profiler) {
    await profiler.send('Profiler.enable');
    await profiler.send('Profiler.start');
  }
  const started = performance.now();
  await input.press('Enter');
  await waitForCanvasLoad(page, version);
  const commitMs = performance.now() - started;
  const commitTasks = await longTasks(page);
  if (profiler) {
    const { profile } = await profiler.send('Profiler.stop');
    await writeFile(`${profilePrefix}.rename.${run}.cpuprofile`, JSON.stringify(profile));
    await profiler.detach();
  }
  await page.locator('.bpmn-canvas').focus();
  const undoVersion = await canvasLoadVersion(page);
  const undoStarted = performance.now();
  await page.keyboard.press('Control+z');
  await waitForCanvasLoad(page, undoVersion);
  return {
    commitMs: +commitMs.toFixed(0),
    blockedMs: commitTasks.totalMs,
    undoMs: +(performance.now() - undoStarted).toFixed(0),
    undoBlockedMs: (await longTasks(page)).totalMs,
  };
}

async function selectionRoundTrip(page, run, phase) {
  const toggle = page.getByRole('switch', { name: 'Properties', exact: true });
  await toggle.check();
  await page.waitForSelector('.bpmn-properties');
  await settledCanvas(page);
  const select = async () => {
    await page.locator('.bpmn-canvas').focus();
    await longTasks(page);
    const started = performance.now();
    await page.keyboard.press('Control+a');
    await page.waitForFunction((count) =>
      document.querySelector('.bpmn-properties__title')?.textContent?.trim() === `${count} elements`,
    expectedShapes, { timeout: 60000 });
    await settledCanvas(page);
    return { completeMs: +(performance.now() - started).toFixed(0), blockedMs: (await longTasks(page)).totalMs };
  };
  const profiler = profilePrefix ? await page.context().newCDPSession(page) : null;
  if (profiler) {
    await profiler.send('Profiler.enable');
    await profiler.send('Profiler.start');
  }
  const cold = await select();
  if (profiler) {
    const { profile } = await profiler.send('Profiler.stop');
    await writeFile(`${profilePrefix}.selection-${phase}.${run}.cpuprofile`, JSON.stringify(profile));
    await profiler.detach();
  }
  await page.locator('.bpmn-canvas').focus();
  await page.keyboard.press('Escape');
  await page.waitForSelector('.bpmn-properties__empty');
  await settledCanvas(page);
  const warm = await select();
  await page.keyboard.press('Escape');
  await page.waitForSelector('.bpmn-properties__empty');
  await toggle.uncheck();
  await settledCanvas(page);
  return { selectedElements: expectedShapes, cold, warm };
}

/** Warm controls isolate panel visibility and selection size without changing the existing edit sequence. */
async function selectionControls(page) {
  const toggle = page.getByRole('switch', { name: 'Properties', exact: true });
  const task = page.locator('.joint-element[data-type="bpmn.Activity"]').first();
  const id = await task.getAttribute('model-id');
  if (!id) throw new Error('Selection control task has no model id.');
  const clear = async () => {
    await page.locator('.bpmn-canvas').focus();
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => /nothing/i.test(document.querySelector('.demo-edit-status')?.textContent ?? ''));
    await settledCanvas(page);
  };
  const select = async (all) => {
    await longTasks(page);
    const started = performance.now();
    if (all) {
      await page.locator('.bpmn-canvas').focus();
      await page.keyboard.press('Control+a');
    } else await task.locator('.bpmn-activity-body').click({ position: { x: 12, y: 12 } });
    await page.waitForFunction((text) => document.querySelector('.demo-edit-status')?.textContent?.includes(text),
      all ? `${expectedShapes}` : id);
    await settledCanvas(page);
    return { completeMs: +(performance.now() - started).toFixed(0), blockedMs: (await longTasks(page)).totalMs };
  };
  await toggle.check();
  await clear();
  const singleOpen = await select(false);
  await clear();
  await toggle.uncheck();
  await settledCanvas(page);
  const singleHidden = await select(false);
  await clear();
  const allHidden = await select(true);
  await clear();
  return { singleOpen, singleHidden, allHidden };
}

async function oneRun(run) {
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
    const previousSummary = await page.locator('.bpmn-canvas__summary').textContent();
    started = performance.now();
    await page.getByRole('button', { name: 'Files', exact: true }).click();
    await page.getByRole('combobox', { name: 'Sample diagram', exact: true }).click();
    await page.locator('fluent-option').filter({ hasText: fixture }).click();
    await page.keyboard.press('Escape');
    await page.waitForFunction(([count, summary]) =>
      document.querySelectorAll('.bpmn-a11y-tree__item').length === count
      && document.querySelector('.bpmn-canvas__summary')?.textContent !== summary,
    [expectedShapes, previousSummary], { timeout: 60000, polling: 'raf' });
    await settledCanvas(page);
    const switchMs = +(performance.now() - started).toFixed(0);
    const switchLongTasks = await longTasks(page);
    const heapAfterLoad = await heapMb(page);

    const nodesAtLoad = await page.evaluate(() => document.querySelectorAll('svg *').length);
    const cellsAtLoad = await page.evaluate(joints);
    const selection = measureSelection ? await selectionRoundTrip(page, run, 'load') : null;
    const edit = await editRoundTrip(page);
    const rename = await renameRoundTrip(page, run);
    const selectionAfterEdit = measureSelection ? await selectionRoundTrip(page, run, 'after-edit') : null;
    const selectionControl = measureSelectionControls ? await selectionControls(page) : null;

    const panOrigin = isolatePan ? await page.evaluate(() => {
      for (const [x, y] of [[700, 450], [900, 650], [1200, 700], [600, 300]]) {
        const target = document.elementFromPoint(x, y);
        if (target?.closest('.bpmn-canvas__paper') && !target.closest('.joint-cell, .joint-tools, .bpmn-lane-splitter'))
          return { x, y };
      }
      throw new Error('No blank canvas point found for isolated pan.');
    }) : { x: 700, y: 450 };
    if (isolatePan) {
      await page.mouse.click(panOrigin.x, panOrigin.y);
      await settledCanvas(page);
    }
    const pan = await sampleFrames(page, async () => {
      await page.mouse.move(panOrigin.x, panOrigin.y);
      await page.mouse.down();
      for (let i = 0; i < 60; i++) await page.mouse.move(panOrigin.x - i * 8, panOrigin.y - i * 4);
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
      coldMs, warmMs, switchMs, edit, rename, editZoomedOut,
      pan, plainWheel, zoom, mountedCells: { min: mountedMin, max: mountedMax },
      atLoad: { svgNodes: nodesAtLoad, mountedCells: cellsAtLoad },
      svgNodesAfterSweep: nodes,
      heapMb: { beforeLoad: heapBefore, afterLoad: heapAfterLoad, end: heapEnd },
      longTasks: { cold: coldLongTasks, switch: switchLongTasks },
      transfer: coldTransfer,
      errors: errors.length,
      isolatedPan: isolatePan,
      ...(measureSelection ? { selection, selectionAfterEdit } : {}),
      ...(measureSelectionControls ? { selectionControls: selectionControl } : {}),
    };
  } finally {
    await browser.close();
  }
}

const results = [];
for (let i = 0; i < runs; i++) results.push(await oneRun(i));
console.log(JSON.stringify({ fixture, runs, ...summarize(results) }, null, 2));
