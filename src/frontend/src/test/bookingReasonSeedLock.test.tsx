import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { ChatProvider } from '../contexts/ChatContext';

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), alert: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'reason-lock-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert }) }));
const ok = (data: unknown) => ({ success: true, data });
const slot = { slotId: 10, doctorId: 2, slotDate: '2030-01-02', startTime: '08:00', endTime: '08:30' };
const defaultGet = async (url: string) => url === '/appointments/online-eligibility' ? ok({ isBlocked: false })
    : url === '/specialties' ? ok([{ id: 1, specialtyCode: 'SP01', specialtyName: 'Nội khoa' }])
        : url.includes('/available-slots') ? ok([slot]) : ok([{ id: 2, fullName: 'Nguyễn Minh' }]);
const mount = () => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter><ChatProvider><BookAppointment /></ChatProvider></MemoryRouter></ConfigProvider>);

beforeEach(() => {
    vi.clearAllMocks(); localStorage.clear(); sessionStorage.clear();
    mocks.get.mockImplementation(defaultGet);
    mocks.post.mockResolvedValue(ok({ id: 42, appointmentCode: 'APT-42' }));
});
afterEach(cleanup);

describe('Booking reason and eligibility regressions', () => {
    it.each([false, true])('preserves typed spaces and trims only the submitted reason (existing draft: %s)', async existingDraft => {
        if (existingDraft) sessionStorage.setItem('cliniccare_booking_draft_reason-lock-patient', JSON.stringify({
            draftId: 'reason-draft', specialtyId: 1, doctorId: 2, slotId: 10, slotDate: slot.slotDate,
            reason: '', version: 1, isComplete: false
        }));
        mount();
        if (!existingDraft) {
            fireEvent.click(await screen.findByRole('button', { name: /^Nội khoa/ }));
            fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn bác sĩ/ }));
            fireEvent.click(await screen.findByRole('button', { name: /Nguyễn Minh/ }));
            fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn giờ khám/ }));
            fireEvent.change(screen.getByLabelText('Chọn ngày khám'), { target: { value: slot.slotDate } });
            fireEvent.click(await screen.findByRole('button', { name: '08:00' }));
        }
        const reason = await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/);
        const user = userEvent.setup();
        await user.type(reason, '  ho khan 3 ngày  ');
        expect(reason).toHaveValue('  ho khan 3 ngày  ');
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/ }));
        expect(screen.getByText('"  ho khan 3 ngày  "', { normalizer: value => value })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận & đặt lịch' }));
        await waitFor(() => expect(mocks.post).toHaveBeenCalledWith('/appointments', expect.objectContaining({ reason: 'ho khan 3 ngày' }), expect.anything()));
    });

    it.each([false, true])('blocks all booking steps when online eligibility is blocked (existing draft: %s)', async existingDraft => {
        if (existingDraft) sessionStorage.setItem('cliniccare_booking_draft_reason-lock-patient', JSON.stringify({
            draftId: 'blocked-draft', specialtyId: 1, doctorId: 2, slotId: 10, slotDate: slot.slotDate, reason: 'Ho khan 3 ngày'
        }));
        mocks.get.mockImplementation((url: string) => url === '/appointments/online-eligibility'
            ? Promise.resolve(ok({ isBlocked: true, message: 'Tạm khóa đặt lịch online đến 09/12/2026. Vui lòng liên hệ lễ tân.' })) : defaultGet(url));
        mount();
        expect(await screen.findByRole('alert')).toHaveTextContent('Tạm khóa đặt lịch online đến 09/12/2026');
        expect(screen.getByRole('link', { name: 'Liên hệ lễ tân' })).toHaveAttribute('href', '/locations');
        expect(screen.queryByRole('button', { name: /^Nội khoa/ })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Tiếp tục|Xác nhận & đặt lịch/ })).not.toBeInTheDocument();
        expect(mocks.post).not.toHaveBeenCalled();
    });

    it('keeps booking inaccessible while eligibility loads and allows retry after a failure', async () => {
        let fail!: (error: Error) => void;
        mocks.get.mockImplementation((url: string) => url === '/appointments/online-eligibility'
            ? new Promise((_resolve, reject) => { fail = reject; }) : defaultGet(url));
        mount();
        expect(await screen.findByText('Đang kiểm tra điều kiện đặt lịch...')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /^Nội khoa/ })).not.toBeInTheDocument();
        await waitFor(() => expect(fail).toBeTypeOf('function'));
        fail(new Error('Mất kết nối'));
        const retry = await screen.findByRole('button', { name: /Thử lại/ });
        mocks.get.mockImplementation(defaultGet);
        fireEvent.click(retry);
        expect(await screen.findByRole('button', { name: /^Nội khoa/ })).toBeInTheDocument();
    });
});
