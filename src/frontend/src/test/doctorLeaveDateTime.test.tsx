import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';
import { DialogProvider } from '../contexts/DialogContext';
import { DoctorLeaveRequests } from '../pages/doctor/DoctorLeaveRequests';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { items: [], totalItems: 0 } });
    vi.mocked(axiosClient.post).mockResolvedValue({ success: true });
});
afterEach(cleanup);

describe('Doctor leave transport dates', () => {
    it('maps the schedule date payload to complete local days', async () => {
        await doctorApi.createLeaveRequest({ startDate: '2026-11-20', endDate: '2026-11-21', reason: 'Việc gia đình' });
        expect(axiosClient.post).toHaveBeenCalledExactlyOnceWith('/doctor/leave-requests', {
            startDateTime: '2026-11-20T00:00:00', endDateTime: '2026-11-21T23:59:59', reason: 'Việc gia đình'
        });
    });

    it('submits the selected clock times without a UTC suffix', async () => {
        render(<ConfigProvider theme={{ token: { motion: false } }}><DialogProvider><DoctorLeaveRequests /></DialogProvider></ConfigProvider>);
        await screen.findByText('Chưa có yêu cầu nghỉ nào.');
        fireEvent.click(screen.getByRole('button', { name: 'Tạo yêu cầu mới' }));
        for (const [label, value] of [['Từ thời gian', '20/11/2099 08:15'], ['Đến thời gian', '21/11/2099 17:45']]) {
            const input = screen.getByLabelText(label);
            fireEvent.change(input, { target: { value } });
            fireEvent.keyDown(input, { key: 'Enter', code: 'Enter' });
            fireEvent.blur(input);
        }
        fireEvent.change(screen.getByLabelText('Lý do nghỉ'), { target: { value: 'Việc gia đình' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }));
        await waitFor(() => expect(axiosClient.post).toHaveBeenCalledExactlyOnceWith('/doctor/leave-requests', {
            startDateTime: '2099-11-20T08:15:00', endDateTime: '2099-11-21T17:45:00', reason: 'Việc gia đình'
        }));
    });
});
