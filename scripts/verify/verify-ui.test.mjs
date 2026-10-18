import assert from 'node:assert/strict';
import { test } from 'node:test';
import { classifyBaseline, matchesPrefix, parseOptions, parseRoutes, roleForRoute, routeSlug } from './verify-ui-lib.mjs';

test('parses static routes, nested paths and role prefixes without confusing public plurals', () => {
    const parsed = parseRoutes(`
        <Routes>
            <Route element={<PublicLayout />}>
                <Route path="/" element={<Home />} />
                <Route path='/doctors' element={<Doctors />} />
                <Route path="/patient" element={<PatientLayout />}>
                    <Route index element={<Profile />} />
                    <Route path="book" element={<Booking />} />
                </Route>
            </Route>
            <Route path={'/doctor/queue'} element={<Queue />} />
            <Route path="/reception" element={<Reception />} />
            <Route path="/diagnostics" element={<Diagnostics />} />
            <Route path="/pharmacy" element={<Pharmacy />} />
            <Route path="/admin" element={<Admin />} />
            <Route path="/notifications" element={<Notifications />} />
            <Route path="/doctor/queue" element={<Duplicate />} />
            {/* <Route path="/ignored-comment" element={<Ignored />} /> */}
        </Routes>`);
    assert.deepEqual(parsed, { routes: [
        { route: '/', role: 'public' }, { route: '/doctors', role: 'public' },
        { route: '/patient', role: 'patient' }, { route: '/patient/book', role: 'patient' },
        { route: '/doctor/queue', role: 'doctor' }, { route: '/reception', role: 'receptionist' },
        { route: '/diagnostics', role: 'technician' }, { route: '/pharmacy', role: 'pharmacist' },
        { route: '/admin', role: 'admin' }, { route: '/notifications', role: 'receptionist' }
    ], skipped: [] });
    assert.equal(roleForRoute('/doctorate'), 'public');
});

test('reports parameter, wildcard, auth, 404 and unavailable LabSupervisor routes as skipped', () => {
    const { routes, skipped } = parseRoutes(`
        <Route path="/medicines/:id" element={<Detail />} />
        <Route path="/files/*" element={<Files />} />
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route path="/404" element={<NotFoundPage />} />
        <Route path="*" element={<NotFoundPage />} />
        <Route path="/lab" element={<Lab />} />
        <Route path="/lab/tat" element={<Report />} />`);
    assert.deepEqual(routes, []);
    assert.equal(skipped.length, 8);
    assert.equal(skipped[0].reason, 'Có tham số');
    assert.equal(skipped.at(-1).reason, 'Thiếu tài khoản LabSupervisor');
});

test('matches baseline by route, viewport, code and literal message, classifying KNOWN, NEW and STALE', () => {
    const baseline = [
        { route: '/pharmacy', viewport: 'desktop', code: 'V1', match: 'existing [error]' },
        { route: '/pharmacy', viewport: 'desktop', code: 'V5', match: 'width' },
        { route: '/admin', viewport: 'mobile', code: 'V1', match: 'not selected' }
    ];
    const result = classifyBaseline([
        { route: '/pharmacy', viewport: 'desktop', issues: [
            { code: 'V1', message: 'prefix existing [error] suffix' }, { code: 'V2', message: '500 api' }
        ] },
        { route: '/pharmacy', viewport: 'mobile', issues: [{ code: 'V1', message: 'existing [error]' }] },
        { route: '/', viewport: 'desktop', issues: [] }
    ], baseline);
    assert.equal(result.hasNew, true);
    assert.deepEqual(result.results.map(row => row.status), ['NEW', 'NEW', 'PASS']);
    assert.deepEqual(result.results[0].issues.map(issue => issue.status), ['KNOWN', 'NEW']);
    assert.equal(result.results[1].issues[0].status, 'NEW');
    assert.deepEqual(result.stale, [baseline[1]]);
});

test('known issues pass while a fixed issue becomes non-failing STALE', () => {
    const baseline = [{ route: '/', viewport: 'desktop', code: 'V1', match: 'old' }];
    const known = classifyBaseline([{ route: '/', viewport: 'desktop', issues: [{ code: 'V1', message: 'old' }] }], baseline);
    assert.equal(known.hasNew, false);
    assert.equal(known.results[0].status, 'KNOWN');
    assert.deepEqual(known.stale, []);
    const fixed = classifyBaseline([{ route: '/', viewport: 'desktop', issues: [] }], baseline);
    assert.equal(fixed.hasNew, false);
    assert.deepEqual(fixed.stale, baseline);
    assert.throws(() => classifyBaseline([], [{ ...baseline[0], match: '' }]), /Invalid baseline/);
});

test('creates safe readable route slugs', () => {
    assert.equal(routeSlug('/'), 'home');
    assert.equal(routeSlug('/pharmacy/prescriptions'), 'pharmacy-prescriptions');
    assert.equal(routeSlug('/patient/health-package-registrations/'), 'patient-health-package-registrations');
    assert.equal(routeSlug('../../admin?path=x'), 'admin-path-x');
});

test('full runs expose baselines for removed routes while filtered runs do not mark other prefixes stale', () => {
    const baseline = [{ route: '/removed', viewport: 'desktop', code: 'V1', match: 'old' }];
    assert.deepEqual(classifyBaseline([], baseline, { only: [], viewports: ['desktop', 'mobile'] }).stale, baseline);
    assert.deepEqual(classifyBaseline([], baseline, { only: ['/pharmacy'], viewports: ['desktop'] }).stale, []);
    assert.deepEqual(classifyBaseline([], baseline, { only: [], viewports: ['mobile'] }).stale, []);
});

test('parses viewport, repeated prefix filters and backend build flag', () => {
    assert.deepEqual(parseOptions(['--only', '/pharmacy', '--only', '/admin', '--viewport', 'mobile', '--skip-build']), {
        only: ['/pharmacy', '/admin'], viewports: ['mobile'], skipBuild: true
    });
    assert.equal(matchesPrefix('/pharmacy/inventory', '/pharmacy'), true);
    assert.equal(matchesPrefix('/pharmacy', '/pharmacy/'), true);
    assert.equal(matchesPrefix('/doctors', '/doctor'), false);
    assert.throws(() => parseOptions(['--viewport', 'tablet']), /desktop or mobile/);
    assert.throws(() => parseOptions(['--only']), /route prefix/);
    assert.throws(() => parseOptions(['--unknown']), /Unknown argument/);
});
