import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const frontendRoot = join(repoRoot, 'src', 'frontend');
const { chromium } = createRequire(join(frontendRoot, 'package.json'))('@playwright/test');
const canaryProject = join(repoRoot, 'src', 'tools', 'ClinicManagement.AI.LiveCanary', 'ClinicManagement.AI.LiveCanary.csproj');
const canaryExecutable = join(repoRoot, 'src', 'tools', 'ClinicManagement.AI.LiveCanary', 'bin', 'Release', 'net10.0', 'ClinicManagement.AI.LiveCanary.exe');
const tempRoot = mkdtempSync(join(tmpdir(), 'cliniccare-browser-e2e-'));
const npmCli = join(dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js');
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

function stop(child) {
    if (!child || child.exitCode !== null) return;
    child.kill();
}

async function startHost(mode) {
    const readyFile = join(tempRoot, `${mode}-${Date.now()}.json`);
    const child = start(canaryExecutable, [
        '--browser-server', '--browser-port', String(providerPort),
        '--browser-provider-mode', mode, '--browser-ready-file', readyFile
    ], repoRoot);
    await waitForFile(readyFile, child);
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
    assert.doesNotMatch(text, /Patient B|bệnh nhân khác/i, 'patient must not receive another patient record');
    results.push({ scenario: '1 patient own appointment', status: 'PASS', evidence: 'UI card CANARY-APT-001; no cross-patient text' });
}

async function sendStaff(page, actor) {
    await page.getByRole('button', { name: `Mở ${roleLabels[actor.name]}` }).click();
    const input = page.getByRole('textbox', { name: 'Nội dung Copilot' });
    await input.fill(actor.message);
    await page.getByRole('button', { name: 'Gửi yêu cầu Copilot' }).click();
    const assistant = page.locator('[data-role="assistant"]').last();
    await assistant.waitFor({ state: 'visible', timeout: 15000 });
    try {
        await page.getByText('Dữ liệu đã kiểm chứng').last().waitFor({ state: 'visible', timeout: 15000 });
    } catch (error) {
        const body = (await page.locator('body').innerText()).slice(0, 3000);
        throw new Error(`${actor.name} assistant did not render a verified result; current=${page.url()}; body=${body}; cause=${error.message}`);
    }
    const body = await page.locator('body').innerText();
    assert.match(body, /ClinicCare domain database|clinic_public_catalog/, `${actor.name} must show a verified source`);
    const displayTool = actor.tool.split('.').at(-1).replaceAll('_', ' ');
    assert.match(body, new RegExp(displayTool.replaceAll(' ', '\\s+'), 'i'), `${actor.name} tool must be present in the permitted tool tray`);
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
        stop(host);
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
        stop(host);
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
        await page.getByRole('textbox', { name: 'Nội dung Copilot' }).fill(actor.message);
        await page.getByRole('button', { name: 'Gửi yêu cầu Copilot' }).click();
        const cancel = page.getByRole('button', { name: 'Dừng yêu cầu Copilot' });
        await cancel.waitFor({ state: 'visible', timeout: 5000 });
        await cancel.click();
        await page.waitForTimeout(500);
        const body = await page.locator('body').innerText();
        assert.doesNotMatch(body, /Tôi đã kiểm tra dữ liệu synthetic/, 'cancelled response must not be rendered');
        results.push({ scenario: '4 client cancellation', status: 'PASS', evidence: 'cancel button removed pending response' });
    } finally {
        await context.close();
        stop(host);
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
        await page.goto(`${appBase}/reception`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(1000);
        const body = await page.locator('body').innerText();
        assert.doesNotMatch(body, /Tôi đã kiểm tra dữ liệu synthetic/, 'old route response must not appear after navigation');
        results.push({ scenario: '4 route change while pending', status: 'PASS', evidence: 'old assistant response not rendered on new route' });
    } finally {
        await context.close();
        stop(host);
    }
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
        for (let runNumber = 1; runNumber <= 3; runNumber++) await runOnlineActors(browser, runNumber);
        await runProviderMode(browser, 'recover', 'Gemini đã phản hồi', true);
        await runProviderMode(browser, 'server-error', 'Đang dùng chế độ dự phòng');
        await runProviderMode(browser, 'rate-limited', 'Đang dùng chế độ dự phòng');
        await runProviderMode(browser, 'disabled', 'AI bị tắt cấu hình; đang dùng hỗ trợ cơ bản');
        await runCancellation(browser);
        await runRouteIsolation(browser);

        // The current browser UI exposes no complete diagnostic publish control
        // and no patient booking confirmation fixture in the isolated seed.
        results.push({ scenario: '5 write preview/confirm', status: 'NOT_COVERED', evidence: 'UI fixture is not complete; HTTP confirmation tests remain authoritative' });
        results.push({ scenario: '6 unpublished/published diagnostic result', status: 'NOT_COVERED', evidence: 'no safe browser publish fixture was added in this acceptance harness' });
    } finally {
        await browser.close();
        stop(vite);
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
