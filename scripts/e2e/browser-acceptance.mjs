import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { DatabaseSync } from 'node:sqlite';
import { createServer } from 'node:net';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const frontendRoot = join(repoRoot, 'src', 'frontend');
const { chromium } = createRequire(join(frontendRoot, 'package.json'))('@playwright/test');
const canaryProject = join(repoRoot, 'src', 'tools', 'ClinicManagement.AI.LiveCanary', 'ClinicManagement.AI.LiveCanary.csproj');
const canaryExecutable = join(repoRoot, 'src', 'tools', 'ClinicManagement.AI.LiveCanary', 'bin', 'Release', 'net10.0', `ClinicManagement.AI.LiveCanary${process.platform === 'win32' ? '.exe' : ''}`);
const tempRoot = mkdtempSync(join(tmpdir(), 'cliniccare-browser-e2e-'));
const npmCli = [
    join(dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js'),
    resolve(dirname(process.execPath), '../lib/node_modules/npm/bin/npm-cli.js')
].find(existsSync);
if (!npmCli) throw new Error('Cannot locate npm-cli.js beside Node.js or in the Unix global npm layout.');
const providerPort = 5318;
const frontendPort = 5418;
const apiBase = `http://127.0.0.1:${providerPort}`;
const appBase = `http://127.0.0.1:${frontendPort}`;
const password = 'Canary@12345';

const actorCases = [
    { name: 'patient', email: 'canary.patient@synthetic.invalid', route: '/patient/ai-consultation', message: 'Tôi muốn xem lịch hẹn đã đặt của tôi.', tool: 'patient.get_my_appointments', patient: true },
    { name: 'receptionist', email: 'canary.reception@synthetic.invalid', route: '/reception/appointments', message: 'Hôm nay quầy tiếp đón có những lượt hẹn nào?', tool: 'reception.get_today_appointments' },
    { name: 'doctor', email: 'canary.doctor@synthetic.invalid', route: '/doctor/appointments', message: 'Hôm nay tôi có những lượt đang chờ được phân công nào?', tool: 'doctor.get_my_queue' },
    { name: 'technician', email: 'canary.technician@synthetic.invalid', route: '/diagnostics', message: 'Các yêu cầu xét nghiệm đang nằm trong hàng đợi của tôi là gì?', tool: 'technician.get_worklist' },
    { name: 'pharmacist', email: 'canary.pharmacist@synthetic.invalid', route: '/pharmacy/prescriptions', message: 'Những toa đang chờ xử lý ở quầy thuốc gồm những toa nào?', tool: 'pharmacist.get_prescription_queue' },
    { name: 'admin', email: 'canary.admin@synthetic.invalid', route: '/admin', message: 'Tình hình vận hành hôm nay của hệ thống thế nào?', tool: 'admin.get_dashboard_metrics' }
];

const roleLabels = {
    receptionist: 'Copilot Lễ tân',
    doctor: 'Copilot Bác sĩ',
    technician: 'Copilot Kỹ thuật viên',
    pharmacist: 'Copilot Dược sĩ',
    admin: 'Copilot Quản trị viên'
};

const results = [];

function run(command, args, cwd, env = {}) {
    const result = spawnSync(command, args, {
        cwd,
        env: { ...process.env, ...env },
        encoding: 'utf8',
        stdio: 'inherit'
    });
    if (result.status !== 0) throw new Error(`${command} ${args.join(' ')} failed with exit ${result.status}: ${result.error?.message ?? ''}`);
}

function start(command, args, cwd, env = {}) {
    const child = spawn(command, args, {
        cwd,
        env: { ...process.env, ...env },
        stdio: ['ignore', 'pipe', 'pipe'],
        windowsHide: true
    });
    let output = '';
    child.stdout.on('data', chunk => { output += chunk.toString(); });
    child.stderr.on('data', chunk => { output += chunk.toString(); });
    child.e2eOutput = () => output;
    return child;
}

async function waitForFile(path, child, timeoutMs = 30000) {
    const started = Date.now();
    while (!existsSync(path)) {
        if (child.exitCode !== null) throw new Error(`Child exited ${child.exitCode}: ${child.e2eOutput()}`);
        if (Date.now() - started > timeoutMs) throw new Error(`Timed out waiting for ${path}: ${child.e2eOutput()}`);
        await delay(100);
    }
    return JSON.parse(readFileSync(path, 'utf8'));
}

async function waitForHttp(url, child, timeoutMs = 30000) {
    const started = Date.now();
    while (true) {
        if (child.exitCode !== null) throw new Error(`Vite exited ${child.exitCode}: ${child.e2eOutput()}`);
        try {
            const response = await fetch(url);
            if (response.ok) return;
        } catch { /* process is still starting */ }
        if (Date.now() - started > timeoutMs) throw new Error(`Timed out waiting for ${url}: ${child.e2eOutput()}`);
        await delay(100);
    }
}

async function waitForPortReleased(port, timeoutMs = 10000) {
    const started = Date.now();
    while (true) {
        const available = await new Promise((resolveAvailable, reject) => {
            const probe = createServer();
            probe.once('error', error => {
                if (error.code === 'EADDRINUSE') resolveAvailable(false);
                else reject(error);
            });
            probe.listen(port, '127.0.0.1', () => probe.close(() => resolveAvailable(true)));
        });
        if (available) return;
        if (Date.now() - started >= timeoutMs) throw new Error(`Port ${port} was not released after stopping the E2E child.`);
        await delay(100);
    }
}

async function stop(child, port) {
    if (!child) return;
    if (child.exitCode === null && child.signalCode === null) {
        let timeout;
        const exited = new Promise(resolveExit => child.once('exit', resolveExit));
        child.kill('SIGTERM');
        try {
            await Promise.race([
                exited,
                new Promise(resolveTimeout => { timeout = setTimeout(resolveTimeout, 10000); })
            ]);
            if (child.exitCode === null && child.signalCode === null) {
                child.kill('SIGKILL');
                await exited;
            }
        } finally {
            clearTimeout(timeout);
        }
    }
    await waitForPortReleased(port);
}

async function startHost(mode, isolatedTemp = false) {
    const readyFile = join(tempRoot, `${mode}-${Date.now()}.json`);
    const hostTemp = isolatedTemp ? join(tempRoot, `local-${Date.now()}`) : null;
    if (hostTemp) mkdirSync(hostTemp, { recursive: true });
    const child = start(canaryExecutable, [
        '--browser-server', '--browser-port', String(providerPort),
        '--browser-provider-mode', mode, '--browser-ready-file', readyFile
    ], repoRoot, hostTemp ? { TMP: hostTemp, TEMP: hostTemp, TMPDIR: hostTemp } : {});
    await waitForFile(readyFile, child);
    if (hostTemp) {
        const databases = readdirSync(hostTemp).filter(name => /^clinic_gate_d_canary_[a-f0-9]{32}\.db$/.test(name));
        assert.equal(databases.length, 1, 'must identify exactly one isolated synthetic SQLite database');
        child.syntheticDatabase = join(hostTemp, databases[0]);
    }
    return child;
}

async function login(page, actor) {
    await page.goto(`${appBase}/login?returnUrl=${encodeURIComponent(actor.route)}`, { waitUntil: 'domcontentloaded' });
    await page.getByLabel('Email hoặc Số điện thoại').fill(actor.email);
    await page.getByRole('textbox', { name: /Mật khẩu/ }).fill(password);
    await page.getByRole('button', { name: /Đăng nhập vào hệ thống/ }).click();
    try {
        await page.waitForURL(url => url.pathname === actor.route, { timeout: 15000 });
    } catch (error) {
        const body = (await page.locator('body').innerText()).slice(0, 1200);
        throw new Error(`Login did not reach ${actor.route}; current=${page.url()}; body=${body}; cause=${error.message}`);
    }
}

async function sendPatient(page, actor) {
    const input = page.getByRole('textbox', { name: 'Nội dung tin nhắn tư vấn AI' });
    await input.fill(actor.message);
    await page.getByRole('button', { name: 'Gửi tin nhắn' }).click();
    const appointmentEvidence = page.getByText('CANARY-APT-001').last();
    try {
        await appointmentEvidence.waitFor({ state: 'visible', timeout: 15000 });
    } catch (error) {
        const body = (await page.locator('body').innerText()).slice(0, 2500);
        throw new Error(`Patient chat did not render the synthetic appointment evidence; current=${page.url()}; body=${body}; cause=${error.message}`);
    }
    const text = await page.locator('body').innerText();
    assert.match(text, /CANARY-APT-001/, 'patient card must contain the synthetic appointment');
    assert.doesNotMatch(text, /CANARY-APT-002|Synthetic Patient B|CANARY-BETA/i, 'patient must not receive secondary synthetic data');
    results.push({ scenario: '1 patient own appointment', status: 'PASS', evidence: 'UI card CANARY-APT-001; secondary facility/patient absent' });
}

async function sendStaff(page, actor) {
    await page.getByRole('button', { name: `Mở ${roleLabels[actor.name]}` }).click();
    const input = page.getByRole('textbox', { name: 'Nội dung Copilot' });
    await input.fill(actor.message);
    const responsePromise = page.waitForResponse(response =>
        response.url().includes('/api/v1/ai/copilot/chat') && response.request().method() === 'POST');
    await page.getByRole('button', { name: 'Gửi yêu cầu Copilot' }).click();
    const copilotResponse = await responsePromise;
    const envelope = await copilotResponse.json();
    const executedToolNames = envelope?.data?.executedToolNames;
    assert.ok(Array.isArray(executedToolNames), `${actor.name} response must expose executedToolNames`);
    assert.ok(executedToolNames.some(name => name.toLowerCase() === actor.tool.toLowerCase()), `${actor.name} response must execute ${actor.tool}`);
    const assistant = page.locator('[data-role="assistant"]').last();
    await assistant.waitFor({ state: 'visible', timeout: 15000 });
    try {
        await page.getByText('Dữ liệu đã kiểm chứng').last().waitFor({ state: 'visible', timeout: 15000 });
    } catch (error) {
        const body = (await page.locator('body').innerText()).slice(0, 3000);
        throw new Error(`${actor.name} assistant did not render a verified result; current=${page.url()}; body=${body}; cause=${error.message}`);
    }
    const body = await page.locator('body').innerText();
    assert.match(body, /Dữ liệu phòng khám|Danh mục phòng khám/, `${actor.name} must show a verified source`);
    assert.doesNotMatch(body, /CANARY-APT-002|Synthetic Patient B|CANARY-BETA/i, `${actor.name} must stay within facility A synthetic scope`);
    assert.equal(await page.getByLabel('Công cụ được phép').count(), 0, 'technical tool tray is removed');
    assert.ok(!body.includes(actor.tool), 'raw tool name must not be visible');
    results.push({ scenario: `3 ${actor.name} scoped copilot`, status: 'PASS', evidence: 'online grounded card and verified source' });
}

async function runOnlineActors(browser, runNumber) {
    const host = await startHost('online');
    try {
        for (const actor of actorCases) {
            const context = await browser.newContext();
            const page = await context.newPage();
            try {
                await login(page, actor);
                if (actor.patient) await sendPatient(page, actor);
                else await sendStaff(page, actor);
            } finally {
                await context.close();
            }
        }
        results.push({ scenario: `online actor run ${runNumber}`, status: 'PASS', evidence: 'six browser actor flows completed' });
    } finally {
        await stop(host, providerPort);
    }
}

async function runProviderMode(browser, mode, expectedState, shouldHaveCard = false) {
    const host = await startHost(mode);
    const context = await browser.newContext();
    const page = await context.newPage();
    try {
        const actor = actorCases[1];
        await login(page, actor);
        await page.getByRole('button', { name: `Mở ${roleLabels[actor.name]}` }).click();
        await page.getByRole('textbox', { name: 'Nội dung Copilot' }).fill(actor.message);
        await page.getByRole('button', { name: 'Gửi yêu cầu Copilot' }).click();
        await page.locator('[data-role="assistant"]').last().waitFor({ state: 'visible', timeout: 15000 });
        await page.getByText(expectedState).last().waitFor({ state: 'visible', timeout: 15000 });
        const body = await page.locator('body').innerText();
        assert.match(body, new RegExp(expectedState), `${mode} must expose honest provider state`);
        if (shouldHaveCard) assert.match(body, /Dữ liệu đã kiểm chứng/);
        else assert.doesNotMatch(body, /Dữ liệu đã kiểm chứng/);
        results.push({ scenario: `2/7 provider ${mode}`, status: 'PASS', evidence: `${expectedState}; grounded card=${shouldHaveCard}` });
    } finally {
        await context.close();
        await stop(host, providerPort);
    }
}

async function runCancellation(browser) {
    const host = await startHost('timeout');
    const context = await browser.newContext();
    const page = await context.newPage();
    try {
        const actor = actorCases[2];
        await login(page, actor);
        await page.getByRole('button', { name: `Mở ${roleLabels[actor.name]}` }).click();
        const input = page.getByRole('textbox', { name: 'Nội dung Copilot' });
        await input.fill(actor.message);
        const requestFailures = [];
        page.on('requestfailed', request => {
            if (request.url().includes('/api/v1/ai/copilot/chat'))
                requestFailures.push(request.failure()?.errorText ?? 'unknown');
        });
        await page.getByRole('button', { name: 'Gửi yêu cầu Copilot' }).click();
        const cancel = page.getByRole('button', { name: 'Dừng yêu cầu Copilot' });
        await cancel.waitFor({ state: 'visible', timeout: 5000 });
        await cancel.click();
        await cancel.waitFor({ state: 'hidden', timeout: 5000 });
        await page.waitForTimeout(500);
        assert.ok(requestFailures.some(error => /ABORT|CANCEL/i.test(error)), `cancelled request must fail as aborted; failures=${requestFailures.join(',')}`);
        assert.equal(await input.isEnabled(), true, 'copilot input must be usable after cancellation');
        const send = page.getByRole('button', { name: 'Gửi yêu cầu Copilot' });
        assert.equal(await send.isEnabled(), false, 'send must be idle after cancellation');
        await input.fill('Tin nhắn mới sau khi hủy.');
        assert.equal(await send.isEnabled(), true, 'a new message must be sendable after cancellation');
        await send.click();
        await cancel.waitFor({ state: 'visible', timeout: 5000 });
        await cancel.click();
        await cancel.waitFor({ state: 'hidden', timeout: 5000 });
        const body = await page.locator('body').innerText();
        assert.doesNotMatch(body, /Tôi đã kiểm tra dữ liệu synthetic/, 'cancelled response must not be rendered');
        results.push({ scenario: '4 client cancellation', status: 'PASS', evidence: 'requestfailed abort; UI idle; a second message could be sent' });
    } finally {
        await context.close();
        await stop(host, providerPort);
    }
}

async function runRouteIsolation(browser) {
    const host = await startHost('delayed');
    const context = await browser.newContext();
    const page = await context.newPage();
    try {
        const actor = actorCases[1];
        await login(page, actor);
        await page.getByRole('button', { name: `Mở ${roleLabels[actor.name]}` }).click();
        await page.getByRole('textbox', { name: 'Nội dung Copilot' }).fill(actor.message);
        await page.getByRole('button', { name: 'Gửi yêu cầu Copilot' }).click();
        await page.getByRole('link', { name: 'Bàn làm việc', exact: true }).first().click();
        await page.waitForURL(url => url.pathname === '/reception', { timeout: 5000 });
        await page.waitForTimeout(1000);
        const body = await page.locator('body').innerText();
        assert.doesNotMatch(body, /Tôi đã kiểm tra dữ liệu synthetic/, 'old route response must not appear after navigation');
        results.push({ scenario: '4 route change while pending', status: 'PASS', evidence: 'old assistant response not rendered on new route' });
    } finally {
        await context.close();
        await stop(host, providerPort);
    }
}

async function runPendingActionCancellation(browser) {
    const host = await startHost('online');
    const context = await browser.newContext();
    const page = await context.newPage();
    const forbiddenWrites = [];
    page.on('request', request => {
        if (request.url().includes('/confirm') || request.url().includes('/diagnostics/orders/1/start'))
            forbiddenWrites.push(`${request.method()} ${request.url()}`);
    });
    try {
        const actor = { ...actorCases[3], route: '/diagnostics/orders/1' };
        await login(page, actor);
        await page.getByRole('button', { name: `Mở ${roleLabels[actor.name]}` }).click();
        await page.locator('summary').filter({ hasText: 'Thao tác có xác nhận' }).click();
        const description = page.getByText('Chuẩn bị tiếp nhận phiếu chỉ định', { exact: true });
        await description.waitFor({ state: 'visible', timeout: 15000 });
        const actionRow = description.locator('..').locator('..');
        const prepareResponsePromise = page.waitForResponse(response =>
            response.url().includes('/api/v1/ai/copilot/actions/prepare') && response.request().method() === 'POST');
        await actionRow.getByRole('button', { name: 'Xem trước' }).click();
        const prepareResponse = await prepareResponsePromise;
        assert.equal(prepareResponse.status(), 200, 'synthetic action prepare must succeed');
        const prepareBody = await prepareResponse.json();
        assert.equal(prepareBody?.status, 'pending_confirmation', 'synthetic fixture must produce a pending preview');
        await page.getByRole('button', { name: 'Hủy thao tác' }).waitFor({ state: 'visible', timeout: 10000 });

        const cancelResponsePromise = page.waitForResponse(response =>
            response.url().includes('/api/v1/ai/copilot/actions/') &&
            response.url().endsWith('/cancel') && response.request().method() === 'POST');
        await page.getByRole('button', { name: 'Hủy thao tác' }).click();
        const cancelResponse = await cancelResponsePromise;
        assert.equal(cancelResponse.status(), 200, 'owned pending action cancel must succeed');
        const cancelBody = await cancelResponse.json();
        assert.equal(cancelBody?.status, 'cancelled', 'cancel response must be explicit');
        await page.getByText(/Đã hủy thao tác chờ xác nhận/).waitFor({ state: 'visible', timeout: 10000 });
        assert.equal(await page.getByRole('button', { name: 'Hủy thao tác' }).count(), 0, 'cancelled preview must leave the UI immediately');
        assert.equal(await page.getByRole('button', { name: /Xác nhận thao tác/ }).count(), 0, 'cancelled preview cannot still be confirmed');
        assert.deepEqual(forbiddenWrites, [], 'cancel must not call confirm or the domain start endpoint');
        results.push({ scenario: 'N1 pending action cancellation', status: 'PASS', evidence: 'synthetic preview cancelled; confirm/domain write absent; preview retired' });
    } finally {
        await context.close();
        await stop(host, providerPort);
    }
}

async function domainData(responsePromise, label) {
    const response = await responsePromise;
    const envelope = await response.json();
    assert.equal(response.status(), 200, `${label}: HTTP ${response.status()}; code=${envelope.errorCode ?? 'none'}`);
    assert.equal(envelope.success, true, `${label} must succeed`);
    assert.ok(envelope.data, `${label} must return domain data`);
    return envelope.data;
}

async function authenticatedHeaders(page) {
    const token = await page.evaluate(() => localStorage.getItem('token'));
    assert.ok(token, 'synthetic actor must have a real login token');
    return { Authorization: `Bearer ${token}` };
}

async function runStaffConfirmationIdempotency(browser) {
    const host = await startHost('online');
    const technicianContext = await browser.newContext();
    const adminContext = await browser.newContext();
    try {
        const page = await technicianContext.newPage();
        const adminPage = await adminContext.newPage();
        await login(page, { ...actorCases[3], route: '/diagnostics/orders/1' });
        await login(adminPage, actorCases[5]);
        const headers = await authenticatedHeaders(page);
        const adminHeaders = await authenticatedHeaders(adminPage);
        const readOrder = () => domainData(page.request.get(`${apiBase}/api/v1/diagnostics/orders/1`, { headers }), 'technician order');
        const before = await readOrder();
        assert.equal(before.orderCode, 'CANARY-ORD-001', 'must use the existing synthetic fixture');
        assert.equal(before.status, 'Ordered');
        const sideEffectIds = async () => {
            const audit = await domainData(adminPage.request.get(`${apiBase}/api/v1/admin/audit-logs?action=DiagnosticOrderStarted&entityName=DiagnosticOrder&page=1&pageSize=100`, { headers: adminHeaders }), 'domain audit');
            assert.equal(audit.items.length, audit.totalItems, 'all matching domain audit records must fit in this synthetic API page');
            return audit.items.filter(item => String(item.entityId) === String(before.id)).map(item => item.id).sort();
        };
        const initialIds = await sideEffectIds();
        assert.equal(initialIds.length, 0, 'Ordered fixture must not have a start side effect');

        await page.getByRole('button', { name: `Mở ${roleLabels.technician}` }).click();
        await page.locator('summary').filter({ hasText: 'Thao tác có xác nhận' }).click();
        const description = page.getByText('Chuẩn bị tiếp nhận phiếu chỉ định', { exact: true });
        await description.waitFor({ state: 'visible', timeout: 15000 });
        const actionRow = description.locator('..').locator('..');
        const prepare = async () => {
            const responsePromise = page.waitForResponse(response => response.url().endsWith('/api/v1/ai/copilot/actions/prepare') && response.request().method() === 'POST');
            await actionRow.getByRole('button', { name: 'Xem trước' }).click();
            const response = await responsePromise;
            assert.equal(response.status(), 200, 'real staff prepare must succeed');
            const result = await response.json();
            assert.equal(result.status, 'pending_confirmation');
            assert.equal(result.preview.resourceId, String(before.id));
            await page.getByRole('button', { name: /Xác nhận thao tác/ }).waitFor({ state: 'visible', timeout: 10000 });
            await page.getByText(`Thông tin đã chọn: ${result.preview.resource.identity}`, { exact: true }).waitFor({ state: 'visible', timeout: 10000 });
            return result;
        };
        await prepare();
        assert.deepEqual(await readOrder(), before, 'preview must not change domain state');
        const cancelPromise = page.waitForResponse(response => response.url().includes('/api/v1/ai/copilot/actions/') && response.url().endsWith('/cancel') && response.request().method() === 'POST');
        await page.getByRole('button', { name: 'Hủy thao tác' }).click();
        const cancelled = await cancelPromise;
        assert.equal(cancelled.status(), 200);
        assert.equal((await cancelled.json()).status, 'cancelled');
        await page.getByText(/Đã hủy thao tác chờ xác nhận/).waitFor({ state: 'visible', timeout: 10000 });
        assert.deepEqual(await readOrder(), before, 'cancel must not change domain state');
        assert.deepEqual(await sideEffectIds(), initialIds, 'cancel must not create domain records');

        const pending = await prepare();
        const confirmUrl = `${apiBase}/api/v1/ai/copilot/actions/${pending.actionId}/confirm`;
        const requestPromise = page.waitForRequest(request => request.url() === confirmUrl && request.method() === 'POST');
        const responsePromise = page.waitForResponse(response => response.url() === confirmUrl && response.request().method() === 'POST');
        await page.getByRole('button', { name: /Xác nhận thao tác/ }).click();
        const confirmRequest = await requestPromise;
        const confirmed = await responsePromise;
        assert.equal(confirmed.status(), 200);
        assert.equal((await confirmed.json()).status, 'completed');
        const after = await readOrder();
        assert.equal(after.status, 'InProgress');
        assert.ok(after.startedAtUtc, 'confirmed start must have its real domain timestamp');
        const executedIds = await sideEffectIds();
        assert.equal(executedIds.length, initialIds.length + 1, 'confirm must create exactly one domain start record');

        const payload = confirmRequest.postDataJSON();
        const replay = await page.request.post(confirmUrl, { headers, data: payload });
        assert.equal(replay.status(), 200, 'same-token replay must succeed');
        const replayResult = await replay.json();
        assert.equal(replayResult.status, 'completed');
        assert.equal(replayResult.data.status, 'already_completed');
        assert.equal(replayResult.isIdempotentReplay, true);
        assert.deepEqual(await readOrder(), after, 'replay must not change domain state again');
        assert.deepEqual(await sideEffectIds(), executedIds, 'replay must not create another side-effect record');

        const invalidToken = (payload.concurrencyToken[0] === 'A' ? 'B' : 'A') + payload.concurrencyToken.slice(1);
        const wrongToken = await page.request.post(confirmUrl, { headers, data: { ...payload, concurrencyToken: invalidToken } });
        assert.equal(wrongToken.status(), 409, 'incorrect token must be refused');
        const wrongTokenResult = await wrongToken.json();
        assert.equal(wrongTokenResult.status, 'failed');
        assert.equal(wrongTokenResult.error.code, 'CONCURRENCY_CONFLICT');
        assert.deepEqual(await readOrder(), after, 'incorrect token must not change domain state');
        assert.deepEqual(await sideEffectIds(), executedIds, 'incorrect token must not create side effects');
        results.push({ scenario: '5 staff preview/cancel/confirm idempotency', status: 'PASS', evidence: 'real UI preview/cancel/confirm; domain audit API count 0 -> 1 -> 1; same-token already_completed/isIdempotentReplay=true; wrong token HTTP 409; no direct DB writes' });
    } finally {
        await technicianContext.close();
        await adminContext.close();
        await stop(host, providerPort);
    }
}

async function runDiagnosticResultVisibility(browser) {
    const host = await startHost('online');
    const technicianContext = await browser.newContext();
    const patientContext = await browser.newContext();
    const doctorContext = await browser.newContext();
    try {
        const technicianPage = await technicianContext.newPage();
        await login(technicianPage, { ...actorCases[3], route: '/diagnostics/orders/1' });
        const headers = await authenticatedHeaders(technicianPage);
        const orderUrl = `${apiBase}/api/v1/diagnostics/orders/1`;
        let order = await domainData(technicianPage.request.get(orderUrl, { headers }), 'Ordered fixture');
        assert.equal(order.orderCode, 'CANARY-ORD-001');
        assert.equal(order.status, 'Ordered');
        assert.equal(order.items.length, 1, 'existing synthetic order must contain one service');
        assert.equal(order.items[0].result, null);

        order = await domainData(technicianPage.request.post(`${orderUrl}/start`, { headers, data: { rowVersion: order.rowVersion } }), 'technician start');
        assert.equal(order.status, 'InProgress');
        const itemId = order.items[0].id;
        const resultText = 'CANARY-E2E-RESULT-REVIEW-001';
        const conclusion = 'CANARY-E2E-CONCLUSION-REVIEW-001';
        order = await domainData(technicianPage.request.put(`${orderUrl}/items/${itemId}/result`, {
            headers, data: { resultText, conclusion, rowVersion: order.items[0].rowVersion }
        }), 'technician record result');
        assert.equal(order.items[0].result.resultText, resultText);
        assert.equal(order.items[0].status, 'Completed');
        order = await domainData(technicianPage.request.post(`${orderUrl}/complete`, { headers, data: { rowVersion: order.rowVersion } }), 'technician complete');
        assert.equal(order.status, 'Completed');
        assert.equal(order.reviewedAtUtc, null);
        assert.equal(order.items[0].result.resultText, resultText, 'technician must see the stored result before doctor review');

        const patientPage = await patientContext.newPage();
        const patientListPath = '/api/v1/patients/me/diagnostic-orders';
        const waitForPatientList = () => patientPage.waitForResponse(response => new URL(response.url()).pathname === patientListPath && response.request().method() === 'GET');
        const hiddenUiResponse = waitForPatientList();
        await login(patientPage, { ...actorCases[0], route: '/patient/diagnostic-results' });
        const patientHeaders = await authenticatedHeaders(patientPage);
        const ownOrder = list => {
            const found = list.items.find(item => item.id === order.id);
            assert.ok(found, 'patient must receive the owned synthetic order');
            return found;
        };
        const assertHidden = patientOrder => {
            assert.equal(patientOrder.status, 'Completed');
            assert.equal(patientOrder.reviewedAtUtc, null);
            assert.equal(patientOrder.items.length, 1);
            assert.equal(patientOrder.items[0].result, null, 'patient Result must remain null before doctor review');
            assert.ok(!JSON.stringify(patientOrder).includes(resultText), 'API must not leak stored result text');
            assert.ok(!JSON.stringify(patientOrder).includes(conclusion), 'API must not leak stored conclusion');
        };
        assertHidden(ownOrder(await domainData(hiddenUiResponse, 'patient UI list before review')));
        assertHidden(ownOrder(await domainData(patientPage.request.get(`${apiBase}${patientListPath}`, { headers: patientHeaders }), 'patient API list before review')));
        await patientPage.getByText(order.orderCode, { exact: true }).waitFor({ state: 'visible', timeout: 10000 });
        await patientPage.getByText('Đang chờ kỹ thuật viên trả kết quả...', { exact: true }).waitFor({ state: 'visible', timeout: 10000 });
        assert.equal(await patientPage.getByText(resultText).count(), 0, 'patient UI must hide the stored result');
        assert.equal(await patientPage.getByText(conclusion).count(), 0, 'patient UI must hide the stored conclusion');

        const doctorPage = await doctorContext.newPage();
        await login(doctorPage, actorCases[2]);
        const doctorHeaders = await authenticatedHeaders(doctorPage);
        const reviewed = await domainData(doctorPage.request.post(`${apiBase}/api/v1/doctor/diagnostic-orders/${order.id}/review`, {
            headers: doctorHeaders, data: { rowVersion: order.rowVersion }
        }), 'doctor review');
        assert.ok(reviewed.reviewedAtUtc, 'real doctor review must set its timestamp');
        const visible = ownOrder(await domainData(patientPage.request.get(`${apiBase}${patientListPath}`, { headers: patientHeaders }), 'patient API list after review'));
        assert.ok(visible.reviewedAtUtc);
        assert.equal(visible.items[0].result.resultText, resultText);
        assert.equal(visible.items[0].result.conclusion, conclusion);
        const visibleUiResponse = waitForPatientList();
        await patientPage.reload({ waitUntil: 'domcontentloaded' });
        const visibleUiOrder = ownOrder(await domainData(visibleUiResponse, 'patient UI list after review'));
        assert.equal(visibleUiOrder.items[0].result.resultText, resultText);
        await patientPage.getByText(`Kết quả: ${resultText}`, { exact: true }).waitFor({ state: 'visible', timeout: 10000 });
        await patientPage.getByText(`Kết luận: ${conclusion}`, { exact: true }).waitFor({ state: 'visible', timeout: 10000 });
        assert.equal(await patientPage.getByText('Đang chờ kỹ thuật viên trả kết quả...', { exact: true }).count(), 0);
        results.push({ scenario: '6 diagnostic result hidden until doctor review', status: 'PASS', evidence: 'Ordered -> technician start/result/complete via real API; patient UI and API Result=null until doctor review; result and conclusion visible in UI/API after review; no direct DB writes' });
    } finally {
        await technicianContext.close();
        await patientContext.close();
        await doctorContext.close();
        await stop(host, providerPort);
    }
}

async function runPatientPendingActionCancellation(browser) {
    // The planner never prepares writes (PLANNER_TOOL_NOT_ALLOWED), so no chat text yields a pending card.
    // This case prepares a REAL pending action through the authenticated prepare endpoint and injects it
    // into the chat response; MedicalChatWidget's cancel button and the backend cancel endpoint are real.
    const host = await startHost('online');
    const context = await browser.newContext();
    const page = await context.newPage();
    const forbiddenWrites = [];
    page.on('request', request => {
        if (request.url().includes('/confirm') || /\/appointments\/[^/]+\/cancel/.test(request.url()))
            forbiddenWrites.push(`${request.method()} ${request.url()}`);
    });
    try {
        const actor = actorCases[0];
        await login(page, actor);
        const token = await page.evaluate(() => localStorage.getItem('token'));
        const headers = { Authorization: `Bearer ${token}` };
        let routeError = null;
        await page.route('**/api/v1/ai/chat', async route => {
            try {
                const sessionId = route.request().postDataJSON()?.sessionId;
                const real = await route.fetch();
                const json = await real.json();
                const appointments = await (await page.request.post(`${apiBase}/api/v1/ai/tools/execute`, {
                    headers, data: { toolName: 'patient.get_my_appointments', toolVersion: '1.0', argumentsJson: '{}', sessionId }
                })).json();
                const findAppointment = value => {
                    if (Array.isArray(value)) {
                        for (const item of value) {
                            const found = findAppointment(item);
                            if (found) return found;
                        }
                    } else if (value && typeof value === 'object') {
                        if (value.appointmentCode === 'CANARY-APT-001') return value;
                        for (const child of Object.values(value)) {
                            const found = findAppointment(child);
                            if (found) return found;
                        }
                    }
                    return null;
                };
                const appointment = findAppointment(appointments);
                const appointmentId = appointment?.appointmentId ?? appointment?.id;
                assert.ok(appointmentId, 'synthetic appointment id not found');
                const prepared = await page.request.post(`${apiBase}/api/v1/ai/copilot/actions/prepare`, {
                    headers, data: {
                        toolName: 'patient.prepare_cancel_appointment', toolVersion: '1.0',
                        argumentsJson: JSON.stringify({ appointmentId, reason: 'Bận việc đột xuất' }),
                        sessionId, conversationId: `conv_${Date.now()}`, idempotencyKey: `idem_${Date.now()}`
                    }
                });
                const result = await prepared.json();
                assert.equal(result?.status, 'pending_confirmation', `real patient prepare must succeed: ${JSON.stringify(result).slice(0, 400)}`);
                json.data = { ...(json.data ?? {}), toolResults: [result] };
                await route.fulfill({ response: real, json });
            } catch (error) { routeError = error; await route.continue().catch(() => {}); }
        });
        await page.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }).click();
        const chat = page.getByRole('dialog', { name: /ClinicCare AI/i });
        await chat.getByRole('textbox', { name: 'Nội dung tin nhắn gửi tới ClinicCare AI' }).fill('Xin chào');
        await chat.getByRole('button', { name: 'Gửi tin nhắn' }).click();
        try { await chat.getByRole('button', { name: 'Hủy thao tác' }).waitFor({ state: 'visible', timeout: 15000 }); }
        catch (error) { throw new Error(`patient preview did not render; routeError=${routeError?.message}`); }

        const cancelResponsePromise = page.waitForResponse(response =>
            response.url().includes('/api/v1/ai/tool-actions/') &&
            response.url().endsWith('/cancel') && response.request().method() === 'POST');
        await chat.getByRole('button', { name: 'Hủy thao tác' }).click();
        const cancelResponse = await cancelResponsePromise;
        assert.equal(cancelResponse.status(), 200, 'patient-owned pending action cancel must succeed');
        assert.equal((await cancelResponse.json())?.status, 'cancelled', 'cancel response must be explicit');
        await page.waitForFunction(() => ![...document.querySelectorAll('button')].some(b => b.textContent?.includes('Hủy thao tác')), null, { timeout: 10000 });
        assert.equal(await page.getByRole('button', { name: /Xác nhận thực hiện/ }).count(), 0, 'cancelled preview cannot still be confirmed');
        assert.deepEqual(forbiddenWrites, [], 'cancel must not call confirm or the appointment cancel endpoint');
        results.push({ scenario: 'N1 patient pending action cancellation', status: 'PASS', evidence: 'real prepare + real /ai/tool-actions/{id}/cancel via MedicalChatWidget button; preview card injected into chat response (planner cannot prepare writes); confirm/domain write absent' });
    } finally {
        await context.close();
        await stop(host, providerPort);
    }
}

async function suggestionCodes(dialog) {
    return dialog.locator('[data-suggestion-code]').evaluateAll(nodes => nodes.map(node => node.getAttribute('data-suggestion-code')));
}

function assertLocalResponse(data) {
    assert.equal(data.providerWasCalled, false, 'suggestion must not call the provider');
    assert.equal(data.providerAttemptCount, 0, 'suggestion provider attempts must stay zero');
    assert.equal(data.plannerMode, 'Deterministic', 'suggestion must use the local planner');
}

async function clickCopilotSuggestion(page, dialog, label, code, tool, cardType) {
    const pending = page.waitForResponse(response => response.url().endsWith('/api/v1/ai/copilot/chat') && response.request().method() === 'POST');
    await dialog.getByRole('button', { name: `Gợi ý: ${label}`, exact: true }).click();
    const response = await pending;
    assert.equal(response.status(), 200, 'suggestion request must succeed');
    assert.equal(response.request().postDataJSON().suggestionCode, code, 'browser must send the selected server code');
    const data = (await response.json()).data;
    assertLocalResponse(data);
    assert.ok(data.executedToolNames.includes(tool), 'expected read tool must execute');
    const card = data.cards.find(item => item.type === cardType);
    assert.ok(card, 'expected result card must be returned');
    await dialog.getByText(card.title, { exact: true }).last().waitFor({ state: 'visible' });
    return data;
}

async function assertNoRawWizardValues(dialog, state) {
    // Inspect text nodes and attributes, including hidden DOM. Payload stays in memory/network only.
    const dom = await dialog.evaluate(root => {
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const texts = [];
        while (walker.nextNode()) texts.push(walker.currentNode.textContent.trim());
        return { texts, attributes: [root, ...root.querySelectorAll('*')].flatMap(node => [...node.attributes].map(attribute => ({
            name: attribute.name, value: attribute.value, svg: node.namespaceURI === 'http://www.w3.org/2000/svg'
        }))) };
    });
    const values = [...dom.texts, ...dom.attributes.map(attribute => attribute.value)];
    const tokens = [...state.options.map(option => option.token), state.backToken, state.reasonToken].filter(Boolean);
    const payload = state.reviewAction?.payload ?? {};
    const opaque = ['sessionId', 'draftId', 'contextSnapshotId', 'confirmationId'].map(key => payload[key]).filter(Boolean);
    assert.ok(!values.some(value => [...tokens, ...opaque].some(secret => value.includes(secret))), 'raw wizard capabilities/identifiers must not enter DOM');
    assert.ok(!values.some(value => /\b(?:specialtyId|doctorId|slotId|patientId|appointmentSlotId|confirmationId|contextSnapshotId|draftId|sessionId)\s*[:=]/i.test(value)), 'DOM must not label internal identifier fields');
    const ids = ['specialtyId', 'doctorId', 'slotId'].map(key => payload[key]).filter(value => value != null).map(String);
    // Drawing coordinates and HTML layout limits are not selection identifiers. Keep all other attributes.
    const geometry = new Set(['x', 'y', 'x1', 'x2', 'y1', 'y2', 'cx', 'cy', 'r', 'rx', 'ry', 'width', 'height', 'stroke-width']);
    const layout = new Set(['rows', 'cols', 'size', 'maxlength', 'minlength']);
    const numericAttributes = dom.attributes.filter(attribute => !(attribute.svg && geometry.has(attribute.name)) && !layout.has(attribute.name));
    const exposedFields = numericAttributes.filter(attribute => ids.includes(attribute.value)).map(attribute => attribute.name);
    assert.ok(!dom.texts.some(value => ids.includes(value)) && exposedFields.length === 0,
        `raw numeric selection identifiers must not appear as DOM text/attributes; matching fields: ${[...new Set(exposedFields)].join(',') || 'none'}`);
}

async function runLocalSuggestionAndWizardAcceptance(browser) {
    const host = await startHost('disabled', true);
    const db = new DatabaseSync(host.syntheticDatabase, { readOnly: true });
    const scenarios = [
        ['patient suggestion chips', async page => {
            await login(page, actorCases[0]);
            await page.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }).click();
            const dialog = page.getByRole('dialog', { name: /ClinicCare AI/i });
            await dialog.getByRole('button', { name: 'Gợi ý: Lịch hẹn của tôi', exact: true }).waitFor({ state: 'visible' });
            assert.deepEqual((await suggestionCodes(dialog)).sort(), [
                'patient.start_booking', 'patient.my_appointments', 'patient.my_visits',
                'patient.my_diagnostic_results', 'patient.my_prescriptions', 'patient.my_bills'
            ].sort(), 'initial menu must contain only the six patient suggestions');
            await clickCopilotSuggestion(page, dialog, 'Lịch hẹn của tôi', 'patient.my_appointments', 'patient.get_my_appointments', 'appointments');
            await dialog.getByText('CANARY-APT-001', { exact: true }).last().waitFor({ state: 'visible' });
            assert.ok((await suggestionCodes(dialog)).every(code => code.startsWith('patient.')), 'response chips must stay patient-only');
            return 'disabled provider; six patient-only menu chips; real click executes own-appointments read and renders synthetic appointment card';
        }],
        ['doctor suggestion chips', async page => {
            await login(page, actorCases[2]);
            await page.getByRole('button', { name: 'Mở Copilot Bác sĩ' }).click();
            let dialog = page.getByRole('dialog', { name: 'Copilot Bác sĩ', exact: true });
            await dialog.getByRole('button', { name: 'Gợi ý: Hôm nay tôi khám ai?', exact: true }).waitFor({ state: 'visible' });
            assert.deepEqual(await suggestionCodes(dialog), ['doctor.my_queue'], 'case chips must be absent on the appointment list');
            await clickCopilotSuggestion(page, dialog, 'Hôm nay tôi khám ai?', 'doctor.my_queue', 'doctor.get_my_queue', 'doctor_queue');
            assert.ok(!(await suggestionCodes(dialog)).some(code => ['doctor.patient_summary', 'doctor.diagnostic_orders', 'doctor.prescription_status'].includes(code)), 'case chips must remain absent without a case');
            const ownAppointment = db.prepare("SELECT Id FROM Appointments WHERE AppointmentCode = 'CANARY-APT-001'").get();
            assert.ok(ownAppointment, 'synthetic assigned appointment must exist');
            await page.goto(`${appBase}/doctor/appointments/${ownAppointment.Id}`, { waitUntil: 'domcontentloaded' });
            await page.getByText('CANARY-APT-001', { exact: false }).first().waitFor({ state: 'visible' });
            await page.getByRole('button', { name: 'Mở Copilot Bác sĩ' }).click();
            dialog = page.getByRole('dialog', { name: 'Copilot Bác sĩ', exact: true });
            await dialog.getByRole('button', { name: 'Gợi ý: Tóm tắt bệnh nhân đang mở', exact: true }).waitFor({ state: 'visible' });
            assert.deepEqual((await suggestionCodes(dialog)).sort(), [
                'doctor.my_queue', 'doctor.patient_summary', 'doctor.diagnostic_orders', 'doctor.prescription_status'
            ].sort(), 'case chips must appear only on the server-verified assigned case');
            return 'disabled provider; real queue chip/card; case chips absent on list and present on assigned appointment detail; doctor-only menu';
        }],
        ...[
            { actor: actorCases[1], codes: ['receptionist.today_appointments', 'receptionist.queue'], buttons: [
                ['Lịch hẹn hôm nay', 'receptionist.today_appointments', 'reception.get_today_appointments', 'reception_appointments'],
                ['Hàng đợi tiếp nhận', 'receptionist.queue', 'reception.get_queue', 'reception_queue']] },
            { actor: actorCases[4], codes: ['pharmacist.prescription_queue', 'pharmacist.inventory'], buttons: [
                ['Đơn thuốc chờ xử lý', 'pharmacist.prescription_queue', 'pharmacist.get_prescription_queue', 'pharmacist_prescription_queue'],
                ['Tồn kho thuốc', 'pharmacist.inventory', 'pharmacist.get_inventory_status', 'pharmacy_inventory']] },
            { actor: actorCases[5], codes: ['admin.dashboard_metrics', 'admin.ai_health'], buttons: [
                ['Chỉ số hôm nay', 'admin.dashboard_metrics', 'admin.get_dashboard_metrics', 'admin_dashboard_metrics'],
                ['Hoạt động của trợ lý AI', 'admin.ai_health', 'admin.get_ai_health', 'admin_ai_health']] }
        ].map(({ actor, codes, buttons }) => [actor.name + ' suggestion chips', async page => {
            await login(page, actor);
            await page.getByRole('button', { name: 'Mở ' + roleLabels[actor.name] }).click();
            const dialog = page.getByRole('dialog', { name: roleLabels[actor.name], exact: true });
            await dialog.getByRole('button', { name: 'Gợi ý: ' + buttons[0][0], exact: true }).waitFor({ state: 'visible' });
            assert.deepEqual((await suggestionCodes(dialog)).sort(), [...codes].sort(), 'initial menu must contain only this role’s current-resource-safe buttons');
            for (const [label, code, tool, card] of buttons) {
                await clickCopilotSuggestion(page, dialog, label, code, tool, card);
                assert.ok((await suggestionCodes(dialog)).every(value => codes.includes(value)), 'reply menu stays role-scoped');
                assert.equal(await dialog.locator('[data-suggestion-code="' + code + '"]').count(), 0, 'just-executed button is excluded');
                assert.equal(await dialog.getByLabel('Công cụ được phép').count(), 0, 'no technical tool tray');
            }
            return 'disabled provider; role-only menu; every resource-free button executes its real read tool and renders a verified card; zero provider attempts';
        }]),
        ['patient booking wizard', async page => {
            await login(page, actorCases[0]);
            await page.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }).click();
            const dialog = page.getByRole('dialog', { name: /ClinicCare AI/i });
            await dialog.getByRole('button', { name: 'Gợi ý: Đặt lịch khám', exact: true }).waitFor({ state: 'visible' });
            const wizard = dialog.getByRole('region', { name: 'Đặt lịch khám từng bước' });
            const before = db.prepare('SELECT COUNT(*) AS count FROM Appointments').get().count;
            assert.ok(db.prepare("SELECT COUNT(*) AS count FROM AppointmentSlots WHERE strftime('%w', SlotDate) = '0' AND IsBooked = 0").get().count > 0, 'seed must include an unbooked Sunday slot');
            const wizardClick = async (button, expectedStep) => {
                const pending = page.waitForResponse(response => response.url().endsWith('/api/v1/ai/booking-wizard') && response.request().method() === 'POST');
                await button.click();
                const response = await pending;
                assert.equal(response.status(), 200, 'wizard step must succeed');
                const state = (await response.json()).data;
                assert.equal(state.step, expectedStep, 'wizard must reach the expected step');
                assert.equal(state.providerWasCalled, false, 'wizard must never call provider');
                assert.ok(!state.errorCode, 'wizard step must not return an error');
                await wizard.getByRole('heading', { name: state.title, exact: true }).waitFor({ state: 'visible' });
                await assertNoRawWizardValues(dialog, state);
                return state;
            };
            let state = await wizardClick(dialog.getByRole('button', { name: 'Gợi ý: Đặt lịch khám', exact: true }), 'specialty');
            state = await wizardClick(wizard.getByRole('button', { name: state.options[0].label, exact: true }), 'doctor');
            state = await wizardClick(wizard.getByRole('button', { name: state.options[0].label, exact: true }), 'day');
            assert.ok(state.options.length > 0, 'bookable dates must exist');
            const renderedDates = (await wizard.getByRole('button').allTextContents()).filter(label => /^\d{2}\/\d{2}\/\d{4}$/.test(label));
            assert.deepEqual(renderedDates, state.options.map(option => option.label), 'date buttons must reflect all server options');
            for (const label of renderedDates) {
                const [day, month, year] = label.split('/').map(Number);
                assert.notEqual(new Date(Date.UTC(year, month - 1, day)).getUTCDay(), 0, 'Sunday must not be a date button');
            }
            state = await wizardClick(wizard.getByRole('button', { name: state.options[0].label, exact: true }), 'slot');
            state = await wizardClick(wizard.getByRole('button', { name: state.options[0].label, exact: true }), 'reason');
            state = await wizardClick(wizard.getByRole('button', { name: 'Khám tổng quát', exact: true }), 'review');
            await dialog.getByRole('heading', { name: 'Tóm tắt thông tin đặt lịch', exact: true }).waitFor({ state: 'visible' });
            assert.equal(db.prepare('SELECT COUNT(*) AS count FROM Appointments').get().count, before, 'review must not create an appointment');
            await dialog.getByRole('button', { name: state.reviewAction.label, exact: true }).click();
            await dialog.getByRole('button', { name: 'Xác nhận đặt lịch', exact: true }).waitFor({ state: 'visible' });
            await assertNoRawWizardValues(dialog, state);
            const pending = page.waitForResponse(response => response.url().endsWith('/api/v1/appointments') && response.request().method() === 'POST');
            await dialog.getByRole('button', { name: 'Xác nhận đặt lịch', exact: true }).click();
            const confirmation = await pending;
            const bookingResult = await confirmation.json();
            const errorCode = bookingResult.errorCode ?? bookingResult.error?.code;
            const safeCode = typeof errorCode === 'string' && /^[A-Z][A-Z0-9_]+$/.test(errorCode) ? errorCode : 'none';
            const validationFields = bookingResult.errors && !Array.isArray(bookingResult.errors) ? Object.keys(bookingResult.errors).filter(key => /^[A-Za-z.]+$/.test(key)).join(',') : 'none';
            assert.equal(confirmation.status(), 201, `existing booking endpoint must create the appointment; HTTP ${confirmation.status()}; code ${safeCode}; validation fields ${validationFields}`);
            assert.ok(confirmation.request().headers()['idempotency-key'], 'existing confirmation must retain its idempotency key');
            assert.equal(confirmation.request().postDataJSON().confirmationId, state.reviewAction.payload.confirmationId, 'existing issued confirmation must be used');
            assert.equal(db.prepare('SELECT COUNT(*) AS count FROM Appointments').get().count, before + 1, 'exactly one new appointment must exist in isolated SQLite');
            assert.equal(db.prepare('SELECT COUNT(*) AS count FROM Appointments WHERE AppointmentSlotId = ?').get(state.reviewAction.payload.slotId).count, 1, 'selected slot must have exactly one appointment');
            return 'disabled provider; real specialty/doctor/day/slot/preset-reason/review/button-confirm flow; Sunday fixture excluded; no raw wizard IDs/tokens in DOM; SQLite appointment count increases by exactly one';
        }]
    ];
    try {
        for (const [scenario, execute] of scenarios) {
            const context = await browser.newContext();
            try {
                const evidence = await execute(await context.newPage());
                results.push({ scenario, status: 'PASS', evidence });
                console.log(`Local browser acceptance PASS: ${scenario}`);
            } catch (error) {
                // Do not print payloads, identifiers, tokens or DOM dumps on failure.
                const location = error.stack?.match(/(?:browser-acceptance|review-ui)\.mjs:(\d+):\d+/)?.[1];
                results.push({ scenario, status: 'FAIL', evidence: error instanceof assert.AssertionError ? error.message.split('\n')[0] : `${error.name ?? 'Browser error'} at acceptance script line ${location ?? 'unknown'}; no payload/DOM recorded` });
                console.log(`Local browser acceptance FAIL: ${scenario}; ${results.at(-1).evidence}`);
            } finally {
                await context.close();
            }
        }
    } finally {
        db.close();
        await stop(host, providerPort);
    }
}


const screenshotsRoot = join(repoRoot, 'TestResults', 'ui-screenshots');

async function assertChatLayout(dialog, mobile, stepNumber) {
    const metrics = await dialog.evaluate((root, narrow) => {
        const visible = element => { const box = element.getBoundingClientRect(); return box.width > 0 && box.height > 0; };
        const nodes = [...root.querySelectorAll('*')];
        const nestedScroll = nodes.filter(element => element.closest('[data-chat-bubble], [data-wizard-step], article') && /auto|scroll/.test(getComputedStyle(element).overflowX + getComputedStyle(element).overflowY)).length;
        const verticalScrollers = nodes.filter(element => visible(element) && /auto|scroll/.test(getComputedStyle(element).overflowY)).length;
        const strips = nodes.filter(element => element.hasAttribute('data-suggestion-strip'));
        const buttons = nodes.filter(element => element.matches('button, a[href="tel:115"]') && visible(element));
        const rgb = value => { const parts = value.match(/[\d.]+/g)?.map(Number) ?? [255, 255, 255]; return [parts[0], parts[1], parts[2], parts[3] ?? 1]; };
        const background = element => {
            if (!element) return [255, 255, 255];
            const [r, g, b, alpha] = rgb(getComputedStyle(element).backgroundColor);
            if (alpha >= 1) return [r, g, b];
            const behind = background(element.parentElement);
            return [r, g, b].map((channel, index) => channel * alpha + behind[index] * (1 - alpha));
        };
        const luminance = channels => channels.slice(0, 3).map(channel => channel / 255).map(channel => channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4).reduce((sum, channel, index) => sum + channel * [.2126, .7152, .0722][index], 0);
        const contrast = element => { const foreground = luminance(rgb(getComputedStyle(element).color)); const behind = luminance(background(element)); return (Math.max(foreground, behind) + .05) / (Math.min(foreground, behind) + .05); };
        const status = root.querySelector('[data-provider-status]');
        const slotGrid = root.querySelector('[data-wizard-step="slot"] [class*="slotGrid"]');
        const wizard = root.querySelector('[data-wizard-step]');
        const messages = root.querySelector('[data-chat-messages]');
        return {
            nestedScroll, verticalScrollers, stripCount: strips.length,
            stripsInComposer: strips.every(element => element.closest('[data-chat-composer]')),
            stripHeight: strips.filter(element => !element.className.includes('grid')).every(element => element.clientHeight <= 96),
            minimumButtonContrast: Math.min(...buttons.map(contrast)),
            buttonsMeetTarget: buttons.every(element => element.getBoundingClientRect().height >= (narrow ? 44 : 40) - .1),
            noOverflow: root.scrollWidth <= root.clientWidth,
            withinViewport: root.getBoundingClientRect().top >= -1 && root.getBoundingClientRect().bottom <= window.innerHeight + 1,
            wizardCount: root.querySelectorAll('[data-wizard-step]').length,
            summaries: root.querySelectorAll('[data-completed-step]').length,
            progress: wizard?.querySelector('[aria-label^="Bước"]')?.getAttribute('aria-label'),
            slotColumns: slotGrid ? getComputedStyle(slotGrid).gridTemplateColumns.split(' ').length : null,
            cardTopVisible: !wizard || wizard.getBoundingClientRect().top >= messages.getBoundingClientRect().top - 1,
            statusLocal: status?.textContent.trim() === 'Chế độ nội bộ' && status?.dataset.tone === 'local',
            technicalText: /(?:doctor|patient|reception|technician|pharmacist|admin)\.get_|PROVIDER_[A-Z_]+|RESOURCE_SCOPE_DENIED|Deterministic|RequiresProvider|NotCalled|Disabled/.test(root.innerText)
        };
    }, mobile);
    assert.equal(metrics.nestedScroll, 0, 'no scrolling descendants in bubbles/cards');
    assert.equal(metrics.verticalScrollers, 1, 'messages must be the only vertical scroller');
    assert.equal(metrics.stripCount, 1, 'exactly one suggestion strip');
    assert.ok(metrics.stripsInComposer, 'suggestions belong to the composer');
    assert.ok(metrics.stripHeight, 'compact suggestions stay within 96 pixels');
    assert.ok(metrics.buttonsMeetTarget, 'every visible button meets the desktop/mobile height target');
    assert.ok(metrics.minimumButtonContrast >= 4.5, 'every visible button has at least 4.5:1 text contrast');
    assert.ok(metrics.noOverflow, 'chat panel has no horizontal overflow');
    assert.ok(metrics.withinViewport, 'header and composer stay inside the viewport');
    assert.ok(metrics.statusLocal, 'disabled provider uses the neutral local label');
    assert.equal(metrics.technicalText, false, 'normal UI must hide raw technical values');
    if (stepNumber) {
        assert.equal(metrics.wizardCount, 1, 'exactly one active wizard');
        assert.ok(metrics.progress?.startsWith('Bước ' + stepNumber + '/6'), 'wizard progress reflects the active step');
        assert.equal(metrics.summaries, stepNumber - 1, 'previous steps are collapsed summary lines');
        assert.ok(metrics.cardTopVisible, 'new step starts at the top of the message viewport');
        if (stepNumber === 4) assert.equal(metrics.slotColumns, 3, 'hours use three columns');
    }
    return metrics;
}

async function runVisualAcceptance(browser) {
    // Capture only to a path Git already ignores; never modify repository ignore rules.
    const ignored = spawnSync('git', ['check-ignore', '--quiet', 'TestResults/ui-screenshots/probe.png'], { cwd: repoRoot, windowsHide: true });
    assert.equal(ignored.status, 0, 'screenshots must be ignored before capture');
    mkdirSync(screenshotsRoot, { recursive: true });
    const host = await startHost('disabled', true);
    try {
        for (const [name, viewport] of [['desktop', { width: 1280, height: 800 }], ['mobile', { width: 390, height: 844 }]]) {
            const mobile = name === 'mobile';
            const context = await browser.newContext({ viewport });
            const page = await context.newPage();
            let captured = 0;
            let minimumContrast = Infinity;
            const capture = async (dialog, label, step) => {
                await dialog.locator('[data-suggestion-strip]').waitFor({ state: 'visible' });
                await dialog.locator('[data-suggestion-strip][aria-busy="true"]').waitFor({ state: 'hidden' });
                await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
                await page.screenshot({ path: join(screenshotsRoot, name + '-' + label + '.png'), animations: 'disabled' });
                minimumContrast = Math.min(minimumContrast, (await assertChatLayout(dialog, mobile, step)).minimumButtonContrast);
                captured++;
            };
            try {
                await login(page, { ...actorCases[0], route: '/patient' });
                await page.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }).click();
                let dialog = page.getByRole('dialog', { name: /ClinicCare AI/i });
                await capture(dialog, 'patient-empty');
                await clickCopilotSuggestion(page, dialog, 'Lịch hẹn của tôi', 'patient.my_appointments', 'patient.get_my_appointments', 'appointments');
                await capture(dialog, 'patient-appointments');
                await dialog.getByRole('textbox').fill('Lịch hẹn của mình');
                const aliasResponse = page.waitForResponse(response => response.url().endsWith('/api/v1/ai/copilot/chat') && response.request().method() === 'POST');
                await dialog.getByRole('button', { name: 'Gửi tin nhắn' }).click();
                const aliasData = (await (await aliasResponse).json()).data;
                assertLocalResponse(aliasData);
                assert.deepEqual(aliasData.executedToolNames, ['patient.get_my_appointments'], 'typed patient alias must use the same real authorized read');
                await dialog.locator('[data-suggestion-strip][aria-busy="true"]').waitFor({ state: 'hidden' });
                const wizard = dialog.getByRole('region', { name: 'Đặt lịch khám từng bước' });
                const clickStep = async (button, expected) => {
                    const pending = page.waitForResponse(response => response.url().endsWith('/api/v1/ai/booking-wizard') && response.request().method() === 'POST');
                    await button.click();
                    const response = await pending;
                    assert.equal(response.status(), 200);
                    const state = (await response.json()).data;
                    assert.equal(state.step, expected);
                    assert.equal(state.providerWasCalled, false);
                    await wizard.getByRole('heading', { name: state.title, exact: true }).waitFor({ state: 'visible' });
                    await assertNoRawWizardValues(dialog, state);
                    return state;
                };
                let state = await clickStep(dialog.getByRole('button', { name: 'Gợi ý: Đặt lịch khám', exact: true }), 'specialty');
                await capture(dialog, 'patient-wizard-specialty', 1);
                for (const [step, number] of [['doctor', 2], ['day', 3], ['slot', 4], ['reason', 5]]) {
                    state = await clickStep(wizard.getByRole('button', { name: state.options[0].label, exact: true }), step);
                    await capture(dialog, 'patient-wizard-' + step, number);
                    if (step === 'reason') {
                        await wizard.getByRole('button', { name: 'Bắt đầu lại đặt lịch', exact: true }).scrollIntoViewIfNeeded();
                        await capture(dialog, 'patient-wizard-reason-input');
                    }
                }
                // Preset reason uses the real existing tokenized step, without any writes.
                state = await clickStep(wizard.getByRole('button', { name: 'Khám tổng quát', exact: true }), 'review');
                await capture(dialog, 'patient-wizard-review', 6);
                await dialog.getByRole('button', { name: 'Bắt đầu lại đặt lịch', exact: true }).scrollIntoViewIfNeeded();
                await capture(dialog, 'patient-wizard-review-summary');
                await dialog.getByRole('button', { name: 'Làm mới cuộc trò chuyện' }).click();
                await dialog.getByRole('textbox').fill('tôi có quyền hạn gì');
                const help = page.waitForResponse(response => response.url().endsWith('/api/v1/ai/copilot/chat') && response.request().method() === 'POST');
                await dialog.getByRole('button', { name: 'Gửi tin nhắn' }).click();
                const helpData = (await (await help).json()).data;
                assertLocalResponse(helpData);
                assert.equal(helpData.intent, 'Help');
                assert.equal(helpData.executedToolNames.length, 0);
                await dialog.getByText('Bạn chọn một gợi ý bên dưới nhé.', { exact: false }).waitFor({ state: 'visible' });
                await capture(dialog, 'patient-help');
                await context.close();
                const staffContext = await browser.newContext({ viewport });
                const staffPage = await staffContext.newPage();
                try {
                    await login(staffPage, actorCases[2]);
                    await staffPage.getByRole('button', { name: 'Mở Copilot Bác sĩ' }).click();
                    dialog = staffPage.getByRole('dialog', { name: 'Copilot Bác sĩ', exact: true });
                    // The capture function below uses this staff page, with the same DOM assertions.
                    const staffCapture = async label => {
                        await dialog.locator('[data-suggestion-strip]').waitFor({ state: 'visible' });
                        await dialog.locator('[data-suggestion-strip][aria-busy="true"]').waitFor({ state: 'hidden' });
                        await staffPage.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
                        await staffPage.screenshot({ path: join(screenshotsRoot, name + '-' + label + '.png'), animations: 'disabled' });
                        minimumContrast = Math.min(minimumContrast, (await assertChatLayout(dialog, mobile)).minimumButtonContrast);
                        captured++;
                    };
                    await staffCapture('doctor-empty');
                    await clickCopilotSuggestion(staffPage, dialog, 'Hôm nay tôi khám ai?', 'doctor.my_queue', 'doctor.get_my_queue', 'doctor_queue');
                    await staffCapture('doctor-queue');
                    await dialog.locator('summary').filter({ hasText: 'Thao tác có xác nhận' }).click();
                    await dialog.locator('details[open]').waitFor({ state: 'visible' });
                    await dialog.getByRole('button', { name: 'Xem trước' }).first().waitFor({ state: 'visible' });
                    await dialog.getByRole('button', { name: 'Xem trước' }).last().scrollIntoViewIfNeeded();
                    await staffCapture('doctor-actions-open');
                    const receptionContext = await browser.newContext({ viewport });
                    try {
                        const receptionPage = await receptionContext.newPage();
                        await login(receptionPage, actorCases[1]);
                        await receptionPage.getByRole('button', { name: 'Mở Copilot Lễ tân' }).click();
                        const receptionDialog = receptionPage.getByRole('dialog', { name: 'Copilot Lễ tân', exact: true });
                        await receptionDialog.locator('[data-suggestion-strip]').waitFor({ state: 'visible' });
                        minimumContrast = Math.min(minimumContrast, (await assertChatLayout(receptionDialog, mobile)).minimumButtonContrast);
                        await receptionPage.screenshot({ path: join(screenshotsRoot, name + '-receptionist-empty.png'), animations: 'disabled' });
                        captured++;
                    } finally { await receptionContext.close(); }
                } finally { await staffContext.close(); }
                assert.equal(captured, 15, 'all requested visual states and reason/review detail views must be captured');
                results.push({ scenario: name + ' visual layout', status: 'PASS', minimumButtonContrast: Number(minimumContrast.toFixed(4)), evidence: '15 ignored screenshots; real local help/read/wizard flows; single scroller/composer strip, target heights, no overflow, progress/summary/time grid, neutral status, technical values hidden' });
                console.log('Visual browser acceptance PASS: ' + name);
            } catch (error) {
                const line = error.stack?.match(/browser-acceptance\.mjs:(\d+):\d+/)?.[1];
                results.push({ scenario: name + ' visual layout', status: 'FAIL', evidence: error instanceof assert.AssertionError ? error.message.split('\n')[0] : (error.name ?? 'Browser error') + ' at line ' + (line ?? 'unknown') + '; no payload/DOM recorded' });
                console.log('Visual browser acceptance FAIL: ' + name + '; ' + results.at(-1).evidence);
            } finally { await context.close(); }
        }
    } finally { await stop(host, providerPort); }
}

async function main() {
    if (process.env.E2E_ALLOW_MUTATION?.toLowerCase() === 'true')
        throw new Error('The isolated browser E2E refuses E2E_ALLOW_MUTATION=true.');
    if (process.env.GEMINI_API_KEY || process.env.GOOGLE_API_KEY)
        throw new Error('The isolated browser E2E refuses ambient Gemini keys.');

    run('dotnet', ['build', canaryProject, '--no-restore', '--configuration', 'Release'], repoRoot);
    run(process.execPath, [npmCli, 'run', 'build'], frontendRoot, { VITE_API_BASE_URL: apiBase });
    const vite = start(process.execPath, [join(frontendRoot, 'node_modules', 'vite', 'bin', 'vite.js'), 'preview', '--host', '127.0.0.1', '--port', String(frontendPort)], frontendRoot, { VITE_API_BASE_URL: apiBase });
    await waitForHttp(appBase, vite);

    const browser = await chromium.launch({ headless: true });
    try {
        await runVisualAcceptance(browser);
        await runLocalSuggestionAndWizardAcceptance(browser);
        for (let runNumber = 1; runNumber <= 3; runNumber++) await runOnlineActors(browser, runNumber);
        await runProviderMode(browser, 'recover', 'Trực tuyến', true);
        await runProviderMode(browser, 'server-error', 'Đang dùng chế độ nội bộ');
        await runProviderMode(browser, 'rate-limited', 'Đang dùng chế độ nội bộ');
        await runProviderMode(browser, 'disabled', 'Chế độ nội bộ');
        await runCancellation(browser);
        await runRouteIsolation(browser);
        await runPendingActionCancellation(browser);
        await runPatientPendingActionCancellation(browser);

        await runStaffConfirmationIdempotency(browser);
        await runDiagnosticResultVisibility(browser);
    } finally {
        await browser.close();
        await stop(vite, frontendPort);
    }

    const failed = results.filter(result => result.status === 'FAIL');
    const summary = {
        isolatedDatabase: 'temporary SQLite file from SyntheticCanaryFactory',
        outboundProvider: 'FakeGeminiHttpHandler; zero Gemini network calls',
        results,
        pass: results.filter(result => result.status === 'PASS').length,
        notCovered: results.filter(result => result.status === 'NOT_COVERED').length,
        fail: failed.length
    };
    console.log(JSON.stringify(summary, null, 2));
    if (failed.length) process.exitCode = 1;
}

try {
    await main();
} finally {
    rmSync(tempRoot, { recursive: true, force: true });
}
