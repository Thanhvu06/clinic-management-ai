import { AxiosError, type AxiosAdapter } from 'axios';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AppointmentLookupModal } from '../components/AppointmentLookupModal';
import { cancelRoleAction, confirmRoleAction, getRoleCopilotCatalog, prepareRoleAction, sendRoleCopilotMessage } from '../api/aiCopilotApi';
import { getCopilotSuggestions } from '../api/aiSuggestionApi';

const rateLimitMessage = 'Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.';
const originalAdapter = axiosClient.defaults.adapter;
const originalUrl = window.location.href;

function rejectResponse(status: number, data: unknown): AxiosAdapter {
    return vi.fn(async (config) => {
        throw new AxiosError(
            `Request failed with status code ${status}`,
            'ERR_BAD_REQUEST',
            config,
            undefined,
            { status, data, statusText: '', headers: {}, config },
        );
    });
}

afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    axiosClient.defaults.adapter = originalAdapter;
    localStorage.removeItem('token');
    window.history.replaceState({}, '', originalUrl);
});

describe('axiosClient response interceptor', () => {
    it.each([
        [undefined, false, false],
        ['ACCESS_DENIED_TO_FACILITY_RESOURCE', false, false],
        ['FORBIDDEN', true, false],
        ['FORBIDDEN', false, true],
    ] as const)('redirects 403 only for explicit FORBIDDEN without suppression (%s, %s)', async (errorCode, suppressForbiddenRedirect, redirect) => {
        const location = { pathname: '/patient', search: '', hash: '', href: '/patient' };
        vi.stubGlobal('window', { location });
        axiosClient.defaults.adapter = rejectResponse(403, errorCode ? { errorCode, message: 'Không có quyền.' } : '');
        await expect(axiosClient.post('/ai/chat', {}, { suppressForbiddenRedirect })).rejects.toMatchObject({ status: 403, code: 'ERR_BAD_REQUEST' });
        expect(location.href).toBe(redirect ? '/forbidden' : '/patient');
    });

    it('all Copilot API modules suppress forbidden redirects and propagate request errors', async () => {
        const location = { pathname: '/patient', search: '', hash: '', href: '/patient' };
        vi.stubGlobal('window', { location });
        const adapter = rejectResponse(403, { errorCode: 'FORBIDDEN' });
        axiosClient.defaults.adapter = adapter;
        const results = await Promise.allSettled([
            sendRoleCopilotMessage('lịch hẹn của tôi'), getRoleCopilotCatalog(),
            prepareRoleAction({ toolName: 'patient.prepare_cancel_appointment', toolVersion: '1.0', argumentsJson: '{}', sessionId: 's', idempotencyKey: 'key' }),
            confirmRoleAction('a', { sessionId: 's', concurrencyToken: 't' }), cancelRoleAction('a', { sessionId: 's' }),
            getCopilotSuggestions({ currentRoute: '/patient' }),
        ]);
        expect(results.every(result => result.status === 'rejected')).toBe(true);
        expect(adapter).toHaveBeenCalledTimes(6);
        for (const [config] of vi.mocked(adapter).mock.calls) expect(config.suppressForbiddenRedirect).toBe(true);
        expect(location.href).toBe('/patient');
    });

    it.each([undefined, '', {}, { message: null }, { message: '' }])(
        'uses the Vietnamese fallback for a 429 payload without a message: %j',
        async (data) => {
            axiosClient.defaults.adapter = rejectResponse(429, data);

            await expect(axiosClient.post('/auth/login', {})).rejects.toMatchObject({
                message: rateLimitMessage,
                status: 429,
                code: 'ERR_BAD_REQUEST',
            });
        },
    );

    it('preserves other server payload fields when adding the 429 fallback', async () => {
        axiosClient.defaults.adapter = rejectResponse(429, { success: false, traceId: 'rate-limit' });

        await expect(axiosClient.get('/appointments/lookup?query=APT-1234')).rejects.toEqual({
            success: false,
            traceId: 'rate-limit',
            message: rateLimitMessage,
            status: 429,
            code: 'ERR_BAD_REQUEST',
        });
    });

    it('preserves a message supplied by the server for 429', async () => {
        axiosClient.defaults.adapter = rejectResponse(429, { message: 'Vui lòng đợi 60 giây.' });

        await expect(axiosClient.post('/auth/register', {})).rejects.toEqual({
            message: 'Vui lòng đợi 60 giây.',
            status: 429,
            code: 'ERR_BAD_REQUEST',
        });
    });

    it('keeps the existing fallback for other HTTP errors', async () => {
        axiosClient.defaults.adapter = rejectResponse(400, '');

        await expect(axiosClient.get('/appointments/lookup')).rejects.toEqual({
            message: 'Request failed with status code 400',
            status: 400,
            code: 'ERR_BAD_REQUEST',
        });
    });

    it('still clears the token for 401 on the login page', async () => {
        window.history.replaceState({}, '', '/login');
        localStorage.setItem('token', 'expired-token');
        axiosClient.defaults.adapter = rejectResponse(401, { message: 'Unauthorized' });

        await expect(axiosClient.get('/auth/me')).rejects.toMatchObject({ message: 'Unauthorized', status: 401 });
        expect(localStorage.getItem('token')).toBeNull();
        expect(window.location.pathname).toBe('/login');
    });

    it('still retains the token for 403 on the forbidden page', async () => {
        window.history.replaceState({}, '', '/forbidden');
        localStorage.setItem('token', 'valid-token');
        axiosClient.defaults.adapter = rejectResponse(403, { message: 'Forbidden' });

        await expect(axiosClient.get('/auth/me')).rejects.toMatchObject({ message: 'Forbidden', status: 403 });
        expect(localStorage.getItem('token')).toBe('valid-token');
        expect(window.location.pathname).toBe('/forbidden');
    });
});

describe('AppointmentLookupModal with the real response interceptor', () => {
    it('shows the Vietnamese message when the lookup API returns an empty 429 response', async () => {
        const adapter = rejectResponse(429, '');
        axiosClient.defaults.adapter = adapter;
        render(<AppointmentLookupModal isOpen onClose={vi.fn()} />);

        fireEvent.change(screen.getByRole('textbox', { name: 'Mã lịch hẹn hoặc số điện thoại' }), {
            target: { value: 'APT-1234' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Tra cứu' }));

        expect(await screen.findByText(rateLimitMessage)).toBeInTheDocument();
        expect(screen.queryByText('Request failed with status code 429')).not.toBeInTheDocument();
        expect(adapter).toHaveBeenCalledWith(expect.objectContaining({
            method: 'get',
            url: '/appointments/lookup?query=APT-1234',
        }));
    });
});
