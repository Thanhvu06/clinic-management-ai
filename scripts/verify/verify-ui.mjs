import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { createServer } from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { classifyBaseline, codes, formatConsoleError, matchesPrefix, parseOptions, parseRoutes, routeSlug, shouldBuildHost, validateBaseline } from './verify-ui-lib.mjs';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const frontendRoot = join(repoRoot, 'src', 'frontend');
const outputRoot = join(repoRoot, '.verify-ui');
const apiPort = 5328;
const frontendPort = 5428;
const apiBase = `http://127.0.0.1:${apiPort}`;
const appBase = `http://127.0.0.1:${frontendPort}`;
const canaryExecutable = join(repoRoot, 'src', 'tools', 'ClinicManagement.AI.LiveCanary', 'bin', 'Release', 'net10.0', `ClinicManagement.AI.LiveCanary${process.platform === 'win32' ? '.exe' : ''}`);
const viewports = { desktop: { width: 1366, height: 768 }, mobile: { width: 390, height: 844 } };
const actors = {
    patient: { email: 'canary.patient@synthetic.invalid', route: '/' },
    receptionist: { email: 'canary.reception@synthetic.invalid', route: '/reception/appointments' },
    doctor: { email: 'canary.doctor@synthetic.invalid', route: '/doctor/appointments' },
    technician: { email: 'canary.technician@synthetic.invalid', route: '/diagnostics' },
    pharmacist: { email: 'canary.pharmacist@synthetic.invalid', route: '/pharmacy/prescriptions' },
    admin: { email: 'canary.admin@synthetic.invalid', route: '/admin' }
};

function start(command, args, cwd, children, env = {}, echo = false) {
    const child = spawn(command, args, {
        cwd, env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true
    });
    children.add(child);
    let output = '';
    const capture = chunk => {
        output += chunk.toString();
        if (echo) process.stdout.write(chunk);
    };
    child.stdout.on('data', capture);
    child.stderr.on('data', capture);
    child.on('error', error => { child.startError = error; });
    child.output = () => output;
    return child;
}

function checkChild(child) {
    if (child.startError) throw child.startError;
    if (child.exitCode !== null || child.signalCode !== null) {
        throw new Error(`Child exited ${child.exitCode ?? child.signalCode}: ${child.output()}`);
    }
}

async function waitForFile(path, child, signal, timeoutMs = 30000) {
    const started = Date.now();
    while (!existsSync(path)) {
        signal.throwIfAborted();
        checkChild(child);
        if (Date.now() - started > timeoutMs) throw new Error(`Timed out waiting for ${path}: ${child.output()}`);
        await delay(100);
    }
    return JSON.parse(readFileSync(path, 'utf8'));
}

async function waitForHttp(url, child, signal, timeoutMs = 30000) {
    const started = Date.now();
    while (true) {
        signal.throwIfAborted();
        checkChild(child);
        try {
            const response = await fetch(url, { signal: AbortSignal.timeout(1000) });
            if (response.ok) return;
        } catch { /* Vite can start listening after its process is created. */ }
        if (Date.now() - started > timeoutMs) throw new Error(`Timed out waiting for ${url}: ${child.output()}`);
        await delay(100);
    }
}

async function waitForPortReleased(port, timeoutMs = 10000) {
    const started = Date.now();
    while (true) {
        const available = await new Promise((resolveAvailable, reject) => {
            const probe = createServer();
            probe.once('error', error => error.code === 'EADDRINUSE' ? resolveAvailable(false) : reject(error));
            probe.listen(port, '127.0.0.1', () => probe.close(() => resolveAvailable(true)));
        });
        if (available) return;
        if (Date.now() - started >= timeoutMs) throw new Error(`Port ${port} is occupied; verify-ui will not stop another process.`);
        await delay(100);
    }
}

async function stop(child) {
    if (!child || child.exitCode !== null || child.signalCode !== null || !child.pid) return;
    if (child.stopping) return child.stopping;
    child.stopping = stopOwnedChild(child);
    return child.stopping;
}

async function stopOwnedChild(child) {
    const exited = new Promise(resolveExit => child.once('close', resolveExit));
    // npm builds have grandchildren on Windows; terminate only this owned process tree.
    if (process.platform === 'win32') {
        await new Promise(resolveKill => {
            const killer = spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
            killer.once('error', resolveKill);
            killer.once('close', resolveKill);
        });
    } else child.kill('SIGTERM');
    let timer;
    try {
        await Promise.race([exited, new Promise(resolveTimeout => { timer = setTimeout(resolveTimeout, 10000); })]);
        if (child.exitCode === null && child.signalCode === null) {
            child.kill('SIGKILL');
            await exited;
        }
    } finally { clearTimeout(timer); }
}

async function run(command, args, cwd, children, signal, env = {}) {
    signal.throwIfAborted();
    const child = start(command, args, cwd, children, env, true);
    const status = await new Promise((resolveExit, reject) => {
        child.once('error', reject);
        child.once('close', resolveExit);
    });
    signal.throwIfAborted();
    if (status !== 0) throw new Error(`${command} ${args.join(' ')} failed with exit ${status}: ${child.output()}`);
}

async function startHost(tempRoot, children, signal) {
    const readyFile = join(tempRoot, 'online.json');
    const child = start(canaryExecutable, [
        '--browser-server', '--browser-port', String(apiPort), '--browser-provider-mode', 'online',
        '--browser-ready-file', readyFile
    ], repoRoot, children, { TMP: tempRoot, TEMP: tempRoot, TMPDIR: tempRoot });
    await waitForFile(readyFile, child, signal);
    return child;
}

async function login(page, actor) {
    await page.goto(`${appBase}/login?returnUrl=${encodeURIComponent(actor.route)}`, { waitUntil: 'domcontentloaded' });
    await page.getByLabel('Email hoặc Số điện thoại').fill(actor.email);
    await page.getByRole('textbox', { name: /Mật khẩu/ }).fill('Canary@12345');
    await page.getByRole('button', { name: /Đăng nhập vào hệ thống/ }).click();
    try {
        await page.waitForURL(url => url.pathname === actor.route, { timeout: 15000 });
    } catch (error) {
        throw new Error(`Login failed for ${actor.email}: ${page.url()}; ${(await page.locator('body').innerText()).slice(0, 1200)}; ${error.message}`);
    }
}

async function inspectPage(context, route, role, viewport, signal) {
    const page = await context.newPage();
    const issues = [];
    const consoleErrors = [];
    const failedResponses = [];
    const add = (code, message) => {
        const existing = issues.find(issue => issue.code === code && issue.message === message);
        if (existing) existing.occurrences++;
        else issues.push({ code, name: codes[code], message, occurrences: 1 });
    };
    page.on('console', message => {
        if (message.type() === 'error') consoleErrors.push({ message: message.text(), url: message.location().url });
    });
    page.on('pageerror', error => add('V1', `${error.name}: ${error.message}`));
    page.on('response', response => {
        if (response.status() >= 400) {
            failedResponses.push({ method: response.request().method(), url: response.url(), status: response.status() });
        }
        if (new URL(response.url()).origin === apiBase && response.status() >= 500) {
            add('V2', `${response.status()} ${response.request().method()} ${response.url()}`);
        }
    });
    page.on('crash', () => add('V4', 'Browser page crashed'));
    const started = Date.now();
    const screenshot = join(role, `${routeSlug(route)}-${viewport}.png`);
    try {
        signal.throwIfAborted();
        await page.setViewportSize(viewports[viewport]);
        try {
            await page.goto(`${appBase}${route}`, { waitUntil: 'domcontentloaded', timeout: 10000 });
            const remaining = Math.max(1, 10000 - (Date.now() - started));
            await page.waitForLoadState('networkidle', { timeout: remaining }).catch(error => {
                if (error.name !== 'TimeoutError') throw error;
            });
        } catch (error) {
            add('V4', `Navigation failed: ${error.message}`);
        }
        signal.throwIfAborted();
        const state = await page.evaluate(() => {
            const main = document.querySelector('main, [role="main"], .ant-layout-content');
            const text = (main?.innerText ?? '').replace(/\s+/g, ' ').trim();
            const headings = Array.from(document.querySelectorAll('h1,h2,h3,[role="heading"],.ant-result-title'))
                .map(node => node.innerText.trim());
            const boundary = document.querySelector('[data-error-boundary], #error-boundary, .error-boundary');
            const crashHeading = headings.find(heading =>
                /^(404(?:\b|\s)|Không tìm thấy trang|Trang không tồn tại|Page not found|Application error|Unexpected Application Error|Something went wrong)/i.test(heading));
            return {
                textLength: text.length, mainFound: Boolean(main), headings,
                crash: boundary?.innerText || crashHeading || null,
                scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth
            };
        });
        if (state.textLength < 20) add('V3', `Main content has ${state.textLength} characters (minimum 20); main found: ${state.mainFound}`);
        const finalPath = new URL(page.url()).pathname;
        if (finalPath === '/login') add('V4', `Redirected to /login from ${route}`);
        if (['/403', '/forbidden'].includes(finalPath) && !['/403', '/forbidden'].includes(route)) {
            add('V4', `Redirected to ${finalPath} from ${route}`);
        }
        if (state.crash) add('V4', `ErrorBoundary / 404: ${state.crash}`);
        if (state.scrollWidth > state.clientWidth + 1) {
            add('V5', `document.documentElement.scrollWidth=${state.scrollWidth} > clientWidth=${state.clientWidth} + 1`);
        }
        mkdirSync(join(outputRoot, role), { recursive: true });
        await page.screenshot({ path: join(outputRoot, screenshot), fullPage: true, timeout: 10000 });
        return { route, role, viewport, finalUrl: page.url(), durationMs: Date.now() - started, screenshot, issues };
    } catch (error) {
        signal.throwIfAborted();
        add('V4', `Page check failed: ${error.message}`);
        let saved = null;
        try {
            mkdirSync(join(outputRoot, role), { recursive: true });
            await page.screenshot({ path: join(outputRoot, screenshot), fullPage: true, timeout: 10000 });
            saved = screenshot;
        } catch (screenshotError) {
            add('V4', `Screenshot failed: ${screenshotError.message}`);
        }
        return { route, role, viewport, finalUrl: page.url(), durationMs: Date.now() - started, screenshot: saved, issues };
    } finally {
        await page.close().catch(() => {});
        for (const error of consoleErrors) add('V1', formatConsoleError(error.message, error.url, failedResponses));
    }
}

function printResults(report) {
    console.log('\nroute | viewport | kết quả | mã lỗi + dòng đầu của lỗi');
    console.log('--- | --- | --- | ---');
    for (const row of report.results) {
        const messages = row.issues.map(issue => `${issue.code} ${issue.name} [${issue.status}]: ${issue.message.split(/\r?\n/)[0]}`);
        console.log(`${row.route} | ${row.viewport} | ${row.status} | ${messages.join('; ') || '—'}`);
    }
    for (const stale of report.stale) console.log(`STALE | ${stale.route} | ${stale.viewport} | ${stale.code}: ${stale.match}`);
    console.log(`\nRan ${report.routeCount} routes / ${report.results.length} page-viewports; skipped ${report.skipped.length} routes; filtered out ${report.filteredOut.length}; ${report.durationMs / 1000}s total.`);
    console.log(`NEW: ${report.results.filter(row => row.status === 'NEW').length}; KNOWN: ${report.results.filter(row => row.status === 'KNOWN').length}; STALE: ${report.stale.length}`);
    console.log(`Report: ${join(outputRoot, 'report.json')}`);
}

async function main() {
    const started = Date.now();
    const options = parseOptions(process.argv.slice(2));
    const parsed = parseRoutes(readFileSync(join(frontendRoot, 'src', 'App.tsx'), 'utf8'));
    const selected = parsed.routes.filter(({ route }) => !options.only.length || options.only.some(prefix => matchesPrefix(route, prefix)));
    if (!selected.length) throw new Error('No static routes match --only.');
    const baseline = validateBaseline(JSON.parse(readFileSync(new URL('./known-issues.json', import.meta.url), 'utf8')));
    console.log('Bỏ qua:');
    parsed.skipped.forEach(({ route, reason }) => console.log(`  ${route} — ${reason}`));
    console.log(`Selected ${selected.length} static routes; viewport: ${options.viewports.join(', ')}.`);
    const children = new Set();
    const controller = new AbortController();
    const tempRoot = mkdtempSync(join(tmpdir(), 'clinic-verify-ui-'));
    const results = [];
    let browser;
    let cleanupPromise;
    let interrupted;
    let fatal;
    const cleanup = () => cleanupPromise ??= (async () => {
        const closed = await Promise.allSettled([browser?.close(), ...Array.from(children, stop)]);
        const errors = closed.filter(result => result.status === 'rejected').map(result => result.reason.message);
        for (const port of [apiPort, frontendPort]) {
            if (children.size) await waitForPortReleased(port).catch(error => errors.push(error.message));
        }
        const tempRelative = relative(resolve(tmpdir()), resolve(tempRoot));
        if (!tempRelative.startsWith('..') && !isAbsolute(tempRelative) && tempRelative.startsWith('clinic-verify-ui-')) {
            rmSync(tempRoot, { recursive: true, force: true });
        } else errors.push(`Refused cleanup outside verify-ui temp root: ${tempRoot}`);
        if (errors.length) throw new Error(`Cleanup failed: ${errors.join('; ')}`);
    })();
    const onSignal = signal => {
        if (interrupted) return;
        interrupted = signal;
        console.error(`\n${signal}: stopping verify-ui host, Vite and browser...`);
        controller.abort(new Error(`Interrupted by ${signal}`));
        void Promise.allSettled([browser?.close(), ...Array.from(children, stop)]);
    };
    const onInterrupt = () => onSignal('SIGINT');
    const onTerminate = () => onSignal('SIGTERM');
    process.on('SIGINT', onInterrupt);
    process.on('SIGTERM', onTerminate);
    mkdirSync(outputRoot, { recursive: true });
    try {
        await waitForPortReleased(apiPort, 1000);
        await waitForPortReleased(frontendPort, 1000);
        if (shouldBuildHost(options)) {
            await run('dotnet', ['build', 'src/backend/ClinicManagement.sln', '-c', 'Release'], repoRoot, children, controller.signal);
            // LiveCanary is not included in the backend solution.
            await run('dotnet', ['build', 'src/tools/ClinicManagement.AI.LiveCanary/ClinicManagement.AI.LiveCanary.csproj', '-c', 'Release'], repoRoot, children, controller.signal);
        }
        if (!existsSync(canaryExecutable)) throw new Error(`LiveCanary Release executable is missing: ${canaryExecutable}. Build it before using --skip-build.`);
        const npmCli = [
            join(dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js'),
            resolve(dirname(process.execPath), '../lib/node_modules/npm/bin/npm-cli.js')
        ].find(existsSync);
        if (!npmCli) throw new Error('Cannot locate npm-cli.js beside Node.js or in the Unix global npm layout.');
        await run(process.execPath, [npmCli, 'run', 'build'], frontendRoot, children, controller.signal, { VITE_API_BASE_URL: apiBase });
        await startHost(tempRoot, children, controller.signal);
        const vite = start(process.execPath, [join(frontendRoot, 'node_modules', 'vite', 'bin', 'vite.js'),
            'preview', '--host', '127.0.0.1', '--port', String(frontendPort), '--strictPort'], frontendRoot, children);
        await waitForHttp(appBase, vite, controller.signal);
        const { chromium } = createRequire(join(frontendRoot, 'package.json'))('@playwright/test');
        browser = await chromium.launch({ headless: true });
        for (const role of new Set(selected.map(route => route.role))) {
            controller.signal.throwIfAborted();
            const context = await browser.newContext({ viewport: viewports.desktop });
            try {
                if (role !== 'public') {
                    const loginPage = await context.newPage();
                    await login(loginPage, actors[role]);
                    await loginPage.close();
                }
                for (const { route } of selected.filter(item => item.role === role)) {
                    for (const viewport of options.viewports) {
                        console.log(`Checking ${route} (${role}, ${viewport})...`);
                        results.push(await inspectPage(context, route, role, viewport, controller.signal));
                    }
                }
            } finally { await context.close().catch(() => {}); }
        }
    } catch (error) {
        fatal = error.stack ?? error.message;
    } finally {
        await cleanup().catch(error => { fatal = `${fatal ?? ''}\n${error.stack ?? error.message}`.trim(); });
        process.off('SIGINT', onInterrupt);
        process.off('SIGTERM', onTerminate);
        const classified = classifyBaseline(results, baseline, fatal ? undefined : options);
        const report = {
            startedAt: new Date(started).toISOString(), durationMs: Date.now() - started, options,
            routeCount: new Set(results.map(row => row.route)).size,
            skipped: parsed.skipped, filteredOut: parsed.routes.filter(route => !selected.includes(route)),
            ...classified, fatal: fatal ?? null, interrupted: interrupted ?? null
        };
        writeFileSync(join(outputRoot, 'report.json'), `${JSON.stringify(report, null, 2)}\n`);
        printResults(report);
        if (fatal) console.error(fatal);
        process.exitCode = interrupted ? (interrupted === 'SIGINT' ? 130 : 143) : fatal || classified.hasNew ? 1 : 0;
    }
}

await main().catch(error => { console.error(error.stack ?? error.message); process.exitCode = 1; });
