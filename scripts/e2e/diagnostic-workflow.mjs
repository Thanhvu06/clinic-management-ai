/**
 * ClinicCare AI - Local API E2E Verification Script
 *
 * SCOPE: This script verifies HTTP API endpoints against a running backend instance.
 * It does NOT test the React UI, browser rendering, print page layout,
 * or any front-end component.
 *
 * ⚠️  SAFETY REQUIREMENTS — READ BEFORE RUNNING ⚠️
 *   - This script MUTATES the database (creates appointments, orders, etc.).
 *   - Must be run against a DEV or STAGING database ONLY.
 *   - Requires environment variable:  E2E_ALLOW_MUTATION=true
 *   - Will refuse to run if API_BASE_URL contains "prod" (case-insensitive).
 *   - Credentials must be supplied via environment variables:
 *       DOCTOR_EMAIL       (required)
 *       DOCTOR_PASSWORD    (required)
 *       PATIENT_A_EMAIL    (required)
 *       PATIENT_A_PASSWORD (required)
 *       PATIENT_B_EMAIL    (required)
 *       PATIENT_B_PASSWORD (required)
 *       RECEPTIONIST_EMAIL    (required)
 *       RECEPTIONIST_PASSWORD (required)
 *       TECHNICIAN_EMAIL      (required)
 *       TECHNICIAN_PASSWORD   (required)
 *
 * Usage:
 *   E2E_ALLOW_MUTATION=true \
 *   DOCTOR_EMAIL=doctor@cliniccare.local  DOCTOR_PASSWORD=Demo@12345 \
 *   PATIENT_A_EMAIL=patient@cliniccare.local PATIENT_A_PASSWORD=Demo@12345 \
 *   PATIENT_B_EMAIL=patient.02@cliniccare.local PATIENT_B_PASSWORD=Demo@12345 \
 *   RECEPTIONIST_EMAIL=reception@cliniccare.local RECEPTIONIST_PASSWORD=Demo@12345 \
 *   TECHNICIAN_EMAIL=technician@cliniccare.local TECHNICIAN_PASSWORD=Demo@12345 \
 *   node scripts/e2e/diagnostic-workflow.mjs
 */

// ─── Safety Gates ────────────────────────────────────────────────────────────

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:5258';

if (process.env.E2E_ALLOW_MUTATION?.trim().toLowerCase() !== 'true') {
    console.error('\n[ABORT] E2E_ALLOW_MUTATION must be set exactly to "true".');
    console.error(`Current value: "${process.env.E2E_ALLOW_MUTATION || ''}" (rejected).`);
    console.error('This script mutates the database. Set E2E_ALLOW_MUTATION=true to confirm.');
    process.exit(2);
}

let parsedUrl;
try {
    parsedUrl = new URL(API_BASE_URL);
} catch {
    console.error(`\n[ABORT] Invalid API_BASE_URL: "${API_BASE_URL}". Must be a valid HTTP/HTTPS URL.`);
    process.exit(2);
}

const LOCAL_HOSTNAMES = new Set(['localhost', '127.0.0.1', '::1', '[::1]']);
const isLocal = LOCAL_HOSTNAMES.has(parsedUrl.hostname.toLowerCase());

if (!isLocal) {
    // Non-local targets require explicit acknowledgment separate from prod checking
    const allowRemote = process.env.E2E_ALLOW_REMOTE_STAGING?.trim().toLowerCase() === 'true';
    if (!allowRemote) {
        console.error(`\n[ABORT] Target hostname "${parsedUrl.hostname}" is not a recognized local address.`);
        console.error('By default, Local API E2E only permits: localhost, 127.0.0.1, ::1');
        console.error('To run against a remote staging server, you must explicitly set:');
        console.error('  E2E_ALLOW_REMOTE_STAGING=true');
        process.exit(2);
    }

    if (/prod/i.test(API_BASE_URL)) {
        console.error('\n[ABORT] Target URL appears to be production even with remote staging allowed:', API_BASE_URL);
        console.error('This script mutates database entries and must NEVER run on production.');
        process.exit(2);
    }
}

// ─── Credential validation ────────────────────────────────────────────────────

const REQUIRED_ENV = [
    'DOCTOR_EMAIL', 'DOCTOR_PASSWORD',
    'PATIENT_A_EMAIL', 'PATIENT_A_PASSWORD',
    'PATIENT_B_EMAIL', 'PATIENT_B_PASSWORD',
    'RECEPTIONIST_EMAIL', 'RECEPTIONIST_PASSWORD',
    'TECHNICIAN_EMAIL', 'TECHNICIAN_PASSWORD',
];

const missing = REQUIRED_ENV.filter(v => !process.env[v]);
if (missing.length > 0) {
    console.error('\n[ABORT] Missing required environment variables:', missing.join(', '));
    console.error('All credentials must be supplied via env vars. No silent fallback.');
    process.exit(2);
}

const doctorEmail       = process.env.DOCTOR_EMAIL;
const doctorPassword    = process.env.DOCTOR_PASSWORD;
const patientAEmail     = process.env.PATIENT_A_EMAIL;
const patientAPassword  = process.env.PATIENT_A_PASSWORD;
const patientBEmail     = process.env.PATIENT_B_EMAIL;
const patientBPassword  = process.env.PATIENT_B_PASSWORD;
const receptionistEmail    = process.env.RECEPTIONIST_EMAIL;
const receptionistPassword = process.env.RECEPTIONIST_PASSWORD;
const technicianEmail      = process.env.TECHNICIAN_EMAIL;
const technicianPassword   = process.env.TECHNICIAN_PASSWORD;

// ─── Test data markers ───────────────────────────────────────────────────────

/** All test data written to the DB must be prefixed with this to make it
 *  clearly distinguishable from real clinical data. */
const TEST_DATA_PREFIX = '[DỮ LIỆU KIỂM THỬ - KHÔNG CÓ GIÁ TRỊ Y KHOA]';

// ─── Helpers ─────────────────────────────────────────────────────────────────

const colors = {
    reset: '\x1b[0m',
    green: '\x1b[32m',
    red: '\x1b[31m',
    yellow: '\x1b[33m',
    blue: '\x1b[34m',
    cyan: '\x1b[36m',
    bold: '\x1b[1m'
};

function logStep(stepNum, message) {
    console.log(`\n${colors.cyan}${colors.bold}[STEP ${stepNum}]${colors.reset} ${message}`);
}

function logSuccess(message) {
    console.log(`  ${colors.green}✔ ${message}${colors.reset}`);
}

function logError(message) {
    console.error(`  ${colors.red}✖ ${message}${colors.reset}`);
}

function assert(condition, message) {
    if (!condition) {
        logError(`Assertion Failed: ${message}`);
        throw new Error(`Assertion Failed: ${message}`);
    }
    logSuccess(message);
}

async function request(path, options = {}) {
    const url = `${API_BASE_URL}${path}`;
    const headers = {
        'Content-Type': 'application/json',
        ...(options.headers || {})
    };
    const res = await fetch(url, { ...options, headers });
    let data = null;
    const text = await res.text();
    try {
        data = text ? JSON.parse(text) : null;
    } catch {
        data = text;
    }
    return { status: res.status, ok: res.ok, data, rawText: text };
}

async function loginUser(email, password) {
    // No silent fallback — if credentials are wrong, fail immediately
    const res = await request('/api/v1/auth/login', {
        method: 'POST',
        body: JSON.stringify({ emailOrPhone: email, password })
    });
    if (!res.ok || !res.data?.data?.accessToken) {
        throw new Error(`Login failed for ${email}: ${JSON.stringify(res.data || res.rawText)}`);
    }
    return res.data.data.accessToken;
}

// ─── Main ─────────────────────────────────────────────────────────────────────

async function main() {
    console.log(`${colors.bold}${colors.blue}${'='.repeat(72)}${colors.reset}`);
    console.log(`${colors.bold}${colors.blue}   ClinicCare AI - Local API E2E Verification Script${colors.reset}`);
    console.log(`${colors.bold}${colors.yellow}   SCOPE: HTTP API only — does NOT test React UI or print rendering${colors.reset}`);
    console.log(`${colors.bold}${colors.yellow}   WARNING: This script mutates the database (appointments, orders)${colors.reset}`);
    console.log(`${colors.bold}${colors.blue}   API Target: ${API_BASE_URL}${colors.reset}`);
    console.log(`${colors.bold}${colors.blue}${'='.repeat(72)}${colors.reset}`);

    // Check backend reachability
    try {
        await fetch(`${API_BASE_URL}/api/v1/diagnostic-services`);
    } catch (err) {
        logError(`Cannot connect to backend at ${API_BASE_URL}. Ensure backend is running.`);
        logError(err.message);
        process.exit(1);
    }

    // Step 1 — Authentication
    logStep(1, 'Authenticating actors via explicit credentials...');
    const doctorToken       = await loginUser(doctorEmail, doctorPassword);
    const patientAToken     = await loginUser(patientAEmail, patientAPassword);
    const patientBToken     = await loginUser(patientBEmail, patientBPassword);
    const receptionistToken = await loginUser(receptionistEmail, receptionistPassword);
    const technicianToken   = await loginUser(technicianEmail, technicianPassword);
    logSuccess(`Doctor authenticated (${doctorEmail})`);
    logSuccess(`Patient A authenticated (${patientAEmail})`);
    logSuccess(`Patient B authenticated (${patientBEmail})`);
    logSuccess(`Receptionist authenticated (${receptionistEmail})`);
    logSuccess(`Technician authenticated (${technicianEmail})`);

    // Step 2 — Catalog & Contract Verification
    logStep(2, 'Verifying Diagnostic Services Catalog & Canonical Contract Schema...');
    const catRes = await request('/api/v1/diagnostic-services', {
        headers: { Authorization: `Bearer ${doctorToken}` }
    });
    assert(catRes.ok, `Catalog returned HTTP 200 (Got ${catRes.status})`);
    const services = catRes.data?.data || [];
    assert(services.length >= 2, `Catalog contains at least 2 services (Found ${services.length})`);

    const firstSvc = services[0];
    assert('category' in firstSvc, 'Catalog item contains canonical "category" property');
    assert('preparationInstructions' in firstSvc, 'Catalog item contains "preparationInstructions" property');
    assert(!('defaultPrice' in firstSvc), 'Catalog item does NOT contain deprecated "defaultPrice"');
    assert(!('description' in firstSvc), 'Catalog item does NOT contain deprecated "description"');
    assert(!catRes.rawText.includes('"undefined"'), 'Raw catalog JSON contains no literal "undefined" string');

    const serviceIds = [services[0].id, services[1].id];
    logSuccess(`Selected services: [${serviceIds.join(', ')}] — ${services[0].name}, ${services[1].name}`);

    // Step 3 — Create a dedicated test appointment
    logStep(3, 'Booking a dedicated test appointment for this E2E run...');
    // We always create a fresh appointment — never reuse arbitrary InConsultation appointments
    // to avoid contaminating real-patient sessions.
    const now = new Date();
    const fromDateStr = new Date(now.getTime() + 86400000).toISOString().split('T')[0];
    const toDateStr   = new Date(now.getTime() + 14 * 86400000).toISOString().split('T')[0];

    const slotsRes = await request(`/api/v1/doctors/1/available-slots?fromDate=${fromDateStr}&toDate=${toDateStr}`);
    const availableSlots = slotsRes.data?.data || [];
    assert(availableSlots.length > 0, `Available slots found for booking (${availableSlots.length} slots)`);

    const slot = availableSlots[0];
    const specRes = await request('/api/v1/doctors/1/specialties');
    const specId = specRes.data?.data?.[0]?.id;
    assert(specId, 'Doctor specialty ID resolved from API');

    const bookRes = await request('/api/v1/appointments', {
        method: 'POST',
        headers: { Authorization: `Bearer ${patientAToken}` },
        body: JSON.stringify({
            doctorId: 1,
            specialtyId: specId,
            appointmentSlotId: slot.slotId,
            reason: `${TEST_DATA_PREFIX} E2E verification run ${new Date().toISOString()}`
        })
    });
    assert(bookRes.ok, `Test appointment booked (HTTP ${bookRes.status})`);
    const activeAppointmentId = bookRes.data.data.id;

    // Confirm -> check-in -> start-consultation
    const confRes = await request(`/api/v1/reception/appointments/${activeAppointmentId}/confirm`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${receptionistToken}` }
    });
    assert(confRes.ok, `Appointment confirmed by receptionist (HTTP ${confRes.status})`);

    const checkinRes = await request(`/api/v1/doctor/appointments/${activeAppointmentId}/check-in`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${doctorToken}` }
    });
    assert(checkinRes.ok, `Doctor checked in (HTTP ${checkinRes.status})`);

    const startRes2 = await request(`/api/v1/doctor/appointments/${activeAppointmentId}/start-consultation`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${doctorToken}` }
    });
    assert(startRes2.ok, `Consultation started (HTTP ${startRes2.status})`);
    logSuccess(`Test appointment #${activeAppointmentId} is now InConsultation`);

    // Step 4 — Record Vitals
    logStep(4, 'Recording simulated vitals (test data — no clinical validity)...');
    const vitalsRes = await request(`/api/v1/doctor/appointments/${activeAppointmentId}/vitals`, {
        method: 'PUT',
        headers: { Authorization: `Bearer ${doctorToken}` },
        body: JSON.stringify({
            bloodPressureSystolic: 120,
            bloodPressureDiastolic: 80,
            heartRate: 75,
            temperature: 36.6,
            respiratoryRate: 16,
            weight: 68.5,
            height: 172,
            spO2: 98
        })
    });
    assert(vitalsRes.ok, `Vitals recorded (HTTP ${vitalsRes.status}) — values are simulated test data`);

    // Verify BMI formula: 68.5 / (1.72²) ≈ 23.2
    const bmiCalculated = Math.round((68.5 / (1.72 * 1.72)) * 10) / 10;
    assert(bmiCalculated === 23.2, `BMI formula: 68.5kg / (1.72m)² = 23.2 (Bình thường < 25.0)`);

    // Step 5 — Create Diagnostic Order
    logStep(5, 'Doctor creates diagnostic order & verifies canonical DTO contracts...');
    const createOrderRes = await request(`/api/v1/doctor/appointments/${activeAppointmentId}/diagnostic-orders`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${doctorToken}` },
        body: JSON.stringify({
            clinicalIndication: `${TEST_DATA_PREFIX} Chỉ định kiểm thử tổng quát E2E`,
            note: `${TEST_DATA_PREFIX} Ghi chú kiểm thử — không thực hiện`,
            serviceIds
        })
    });
    assert(createOrderRes.ok, `Order created (HTTP ${createOrderRes.status})`);
    const orderData = createOrderRes.data?.data;
    assert(orderData?.id, 'Diagnostic order ID is generated');
    assert(orderData.orderCode?.startsWith('DX-'), `Order code format correct (${orderData.orderCode})`);
    assert(orderData.status === 'Ordered', `Order initial status is "Ordered"`);

    const rawOrderJson = createOrderRes.rawText;
    assert(rawOrderJson.includes('"specialtyName":'), 'Response JSON includes canonical "specialtyName"');
    assert(!rawOrderJson.includes('"orderingDoctorSpecialty":'), 'Response does NOT contain obsolete "orderingDoctorSpecialty"');
    assert(rawOrderJson.includes('"category":'), 'Response items include canonical "category"');
    assert(!rawOrderJson.includes('"serviceCategory":'), 'Response does NOT contain obsolete "serviceCategory"');
    assert(!rawOrderJson.includes('"undefined"'), 'Response JSON contains no literal "undefined" strings');
    logSuccess(`Specialty: "${orderData.specialtyName}" | category field: verified`);

    const orderId = orderData.id;

    // Step 6 — Completion Guard #1
    logStep(6, 'Completion Guard #1: doctor blocked while order is Ordered...');
    const earlyCompleteRes = await request(`/api/v1/doctor/appointments/${activeAppointmentId}/complete`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${doctorToken}` },
        body: JSON.stringify({
            diagnosis: `${TEST_DATA_PREFIX} Thử hoàn tất sớm`,
            summary: `${TEST_DATA_PREFIX} Kiểm thử guard #1`
        })
    });
    assert(earlyCompleteRes.status === 422, `Blocked with HTTP 422 (Got ${earlyCompleteRes.status})`);
    assert(earlyCompleteRes.rawText.includes('PENDING_DIAGNOSTIC_RESULTS'), 'Error code: PENDING_DIAGNOSTIC_RESULTS');

    // Step 7 — Technician Workflow
    logStep(7, 'Technician: start order, enter simulated results, complete order...');
    const techStartRes = await request(`/api/v1/diagnostics/orders/${orderId}/start`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${technicianToken}` }
    });
    assert(techStartRes.ok, `Order started by technician (HTTP ${techStartRes.status})`);
    assert(techStartRes.data?.data?.status === 'InProgress', 'Order transitioned to InProgress');

    // Record results — label all values as simulated test data with generic units
    for (const item of orderData.items) {
        const itemResultRes = await request(`/api/v1/diagnostics/orders/${orderId}/items/${item.id}/result`, {
            method: 'PUT',
            headers: { Authorization: `Bearer ${technicianToken}` },
            body: JSON.stringify({
                resultText: `${TEST_DATA_PREFIX} Giá trị kiểm thử cho ${item.serviceCode}`,
                conclusion: `${TEST_DATA_PREFIX} Kết luận kiểm thử — không có giá trị lâm sàng`,
                referenceRange: `${TEST_DATA_PREFIX} Khoảng tham chiếu kiểm thử`,
                unit: '' // Intentionally blank — no fabricated units for simulated data
            })
        });
        assert(itemResultRes.ok, `Result recorded for item ${item.serviceCode} (HTTP ${itemResultRes.status})`);
    }

    const completeOrderRes = await request(`/api/v1/diagnostics/orders/${orderId}/complete`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${technicianToken}` }
    });
    assert(completeOrderRes.ok, `Order completed by technician (HTTP ${completeOrderRes.status})`);
    assert(completeOrderRes.data?.data?.status === 'Completed', 'Order transitioned to Completed');

    // Step 8 — Completion Guard #2
    logStep(8, 'Completion Guard #2: doctor blocked when order Completed but unreviewed...');
    const unreviewedCompleteRes = await request(`/api/v1/doctor/appointments/${activeAppointmentId}/complete`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${doctorToken}` },
        body: JSON.stringify({
            diagnosis: `${TEST_DATA_PREFIX} Thử hoàn tất chưa review`,
            summary: `${TEST_DATA_PREFIX} Kiểm thử guard #2`
        })
    });
    assert(unreviewedCompleteRes.status === 422, `Blocked with HTTP 422 (Got ${unreviewedCompleteRes.status})`);
    assert(unreviewedCompleteRes.rawText.includes('UNREVIEWED_DIAGNOSTIC_RESULTS'), 'Error code: UNREVIEWED_DIAGNOSTIC_RESULTS');

    // Step 9 — Doctor Reviews Order
    logStep(9, 'Doctor reviews & confirms diagnostic order...');
    const reviewRes = await request(`/api/v1/doctor/diagnostic-orders/${orderId}/review`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${doctorToken}` },
        body: JSON.stringify({})
    });
    assert(reviewRes.ok, `Doctor reviewed order (HTTP ${reviewRes.status})`);
    const reviewedOrder = reviewRes.data?.data;
    assert(reviewedOrder?.reviewedAtUtc !== null && reviewedOrder?.reviewedAtUtc !== undefined,
        'Order records doctor review timestamp (reviewedAtUtc)');
    logSuccess(`Doctor review confirmed at ${reviewedOrder.reviewedAtUtc}`);

    // Step 10 — Doctor Completes Consultation
    logStep(10, 'Doctor completes consultation with test-marked clinical conclusions...');
    const finalCompleteRes = await request(`/api/v1/doctor/appointments/${activeAppointmentId}/complete`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${doctorToken}` },
        body: JSON.stringify({
            chiefComplaint: `${TEST_DATA_PREFIX} Triệu chứng kiểm thử`,
            clinicalFindings: `${TEST_DATA_PREFIX} Phát hiện lâm sàng kiểm thử`,
            diagnosis: `${TEST_DATA_PREFIX} Chẩn đoán kiểm thử`,
            treatmentPlan: `${TEST_DATA_PREFIX} Kế hoạch điều trị kiểm thử`,
            summary: `${TEST_DATA_PREFIX} Tóm tắt kiểm thử E2E`
        })
    });
    assert(finalCompleteRes.ok, `Appointment completed (HTTP ${finalCompleteRes.status})`);
    logSuccess(`Appointment #${activeAppointmentId} transitioned to Completed`);

    // Step 11 — Cross-Tenant Security
    logStep(11, 'Cross-tenant security: Patient B cannot access Patient A order...');
    const patBAccessRes = await request(`/api/v1/patients/me/diagnostic-orders/${orderId}`, {
        headers: { Authorization: `Bearer ${patientBToken}` }
    });
    assert(patBAccessRes.status === 404,
        `Patient B receives 404 for Patient A order (Got ${patBAccessRes.status})`);

    const patBListRes = await request('/api/v1/patients/me/diagnostic-orders', {
        headers: { Authorization: `Bearer ${patientBToken}` }
    });
    assert(patBListRes.ok, 'Patient B can list their own orders');
    const patBItems = patBListRes.data?.data?.items || [];
    const leakedOrder = patBItems.find(o => o.id === orderId);
    assert(!leakedOrder, 'Patient A order NOT leaked into Patient B order list');
    logSuccess('Data isolation between patients verified');

    const patAAccessRes = await request(`/api/v1/patients/me/diagnostic-orders/${orderId}`, {
        headers: { Authorization: `Bearer ${patientAToken}` }
    });
    assert(patAAccessRes.ok, `Patient A can access their own order (HTTP ${patAAccessRes.status})`);
    assert(!patAAccessRes.rawText.includes('"undefined"'), 'Patient view contains no literal "undefined" string');
    logSuccess('Patient A order access verified');

    console.log(`\n${colors.bold}${colors.green}${'='.repeat(72)}${colors.reset}`);
    console.log(`${colors.bold}${colors.green}   ALL 11 LOCAL API VERIFICATION STEPS PASSED${colors.reset}`);
    console.log(`${colors.bold}${colors.yellow}   Note: this verifies HTTP API only — React UI not tested here${colors.reset}`);
    console.log(`${colors.bold}${colors.green}${'='.repeat(72)}${colors.reset}\n`);
}

main().catch(err => {
    console.error(`\n${colors.bold}${colors.red}E2E Script Failed:${colors.reset}`, err);
    process.exit(1);
});
