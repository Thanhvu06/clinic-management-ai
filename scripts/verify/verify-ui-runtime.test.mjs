import assert from 'node:assert/strict';
import { test } from 'node:test';
import { formatConsoleError, parseOptions, shouldBuildHost } from './verify-ui-lib.mjs';

test('rebuilds the host by default even when a Release executable is already present', () => {
    assert.equal(shouldBuildHost(), true);
    assert.equal(shouldBuildHost(parseOptions([])), true);
    assert.equal(shouldBuildHost({ ...parseOptions(['--only', '/pharmacy']), executableExists: true }), true);
});

test('skips the host build only with the explicit skip-build flag', () => {
    assert.equal(shouldBuildHost(parseOptions(['--skip-build'])), false);
    assert.equal(shouldBuildHost(parseOptions(['--viewport', 'mobile'])), true);
});

test('adds the matching failing response method, URL and status to resource console errors', () => {
    const url = 'http://127.0.0.1:5328/api/v1/pharmacy/batches?page=1&pageSize=10';
    const message = 'Failed to load resource: the server responded with a status of 404 (Not Found)';
    const responses = [
        { method: 'POST', url, status: 500 },
        { method: 'GET', url: 'http://127.0.0.1:5328/api/v1/other', status: 404 },
        { method: 'GET', url, status: 404 }
    ];
    assert.equal(formatConsoleError(message, url, responses), `${message} [GET ${url} -> 404]`);
});

test('correlates resource errors after responses arrive, including a unique response without a console URL', () => {
    const message = 'Failed to load resource: the server responded with a status of 503 (Service Unavailable)';
    const response = { method: 'GET', url: 'http://127.0.0.1:5328/api/v1/medicines', status: 503 };
    assert.equal(formatConsoleError(message, '', [response]), `${message} [GET ${response.url} -> 503]`);
});

test('preserves ordinary console errors and does not attribute unrelated or ambiguous responses', () => {
    const message = 'Failed to load resource: the server responded with a status of 404 (Not Found)';
    const responses = [
        { method: 'GET', url: 'http://127.0.0.1:5328/one', status: 404 },
        { method: 'GET', url: 'http://127.0.0.1:5328/two', status: 404 }
    ];
    assert.equal(formatConsoleError('x', '', responses), 'x');
    assert.equal(formatConsoleError(message, 'http://127.0.0.1:5328/missing', responses), message);
    assert.equal(formatConsoleError(message, '', responses), message);
    assert.equal(formatConsoleError(message, responses[0].url, [{ ...responses[0], status: 200 }]), message);
});
