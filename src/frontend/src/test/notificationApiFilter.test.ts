import { beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { notificationApi } from '../api/notificationApi';
import { NotificationType } from '../types/notification';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));

describe('notification API type query contract', () => {
    beforeEach(() => vi.clearAllMocks());

    it.each([NotificationType.System, NotificationType.Appointment, NotificationType.Diagnostic])('sends type %i without changing the existing argument order', async type => {
        await notificationApi.getMyNotifications(3, 15, false, type);
        const url = new URL(vi.mocked(axiosClient.get).mock.calls[0][0], 'http://test');
        expect(Object.fromEntries(url.searchParams)).toEqual({ page: '3', pageSize: '15', isRead: 'false', type: String(type) });
    });

    it('omits optional filters for existing callers', async () => {
        await notificationApi.getMyNotifications(1, 5);
        const url = new URL(vi.mocked(axiosClient.get).mock.calls[0][0], 'http://test');
        expect(Object.fromEntries(url.searchParams)).toEqual({ page: '1', pageSize: '5' });
    });
});
