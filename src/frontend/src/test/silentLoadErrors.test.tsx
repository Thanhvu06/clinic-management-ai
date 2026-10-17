import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';
import { DialogProvider } from '../contexts/DialogContext';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { DoctorLeaveRequests } from '../pages/doctor/DoctorLeaveRequests';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import { DoctorAppointments } from '../pages/doctor/DoctorAppointments';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getSchedule: vi.fn(), previewLeave: vi.fn(), createLeaveRequest: vi.fn() } }));
const ok = <T,>(data: T) => ({ success: true, message: '', data });
const mount = (page: ReactNode) => render(<ConfigProvider theme={{ token: { motion: false } }}>
    <MemoryRouter><CopilotResourceProvider><DialogProvider>{page}</DialogProvider></CopilotResourceProvider></MemoryRouter>
</ConfigProvider>);
const setDate = (label: string, value: string) => {
    const input = screen.getByLabelText(label);
    fireEvent.change(input, { target: { value } });
    fireEvent.keyDown(input, { key: 'Enter', code: 'Enter' });
    fireEvent.blur(input);
};
const openLeave = async () => {
    mount(<DoctorSchedule />);
    await screen.findByText('Không có lịch làm việc cho tuần này');
    fireEvent.click(screen.getByRole('button', { name: 'Đăng ký nghỉ phép' }));
    setDate('Từ ngày', '20/03/2099');
    setDate('Đến ngày', '21/03/2099');
};

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockResolvedValue(ok({ items: [], totalItems: 0 }));
    vi.mocked(doctorApi.getSchedule).mockResolvedValue(ok([]));
    vi.mocked(doctorApi.previewLeave).mockResolvedValue(ok({ startDate: '', endDate: '', affectedAppointmentCount: 0, affectedAppointments: [] }));
    vi.mocked(doctorApi.createLeaveRequest).mockResolvedValue(ok(undefined));
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('Visible load errors and retry', () => {
    it.each([
        ['leave requests', DoctorLeaveRequests, '/doctor/leave-requests?', 'Chưa có yêu cầu nghỉ nào.'],
        ['reception appointments', ReceptionAppointments, '/reception/appointments?', 'Không tìm thấy lịch hẹn nào.']
    ] as const)('%s shows InlineError rather than empty data and retries the same API', async (_, Page, endpoint, empty) => {
        vi.mocked(axiosClient.get).mockRejectedValueOnce({ message: 'Máy chủ chưa tải được dữ liệu.', status: 503 });
        mount(<Page />);
        await screen.findByText('Máy chủ chưa tải được dữ liệu.');
        expect(screen.queryByText(empty)).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText(empty);
        expect(axiosClient.get).toHaveBeenCalledTimes(2);
        expect(vi.mocked(axiosClient.get).mock.calls.every(([url]) => url.startsWith(endpoint))).toBe(true);
        expect(vi.mocked(axiosClient.get).mock.calls[0]).toEqual(vi.mocked(axiosClient.get).mock.calls[1]);
        expect(screen.queryByText('Máy chủ chưa tải được dữ liệu.')).not.toBeInTheDocument();
    });

    it('shows preview failure without the no-affected-appointments reassurance and retries', async () => {
        vi.mocked(doctorApi.previewLeave).mockRejectedValueOnce({ response: { data: { message: 'Dịch vụ xem trước đang lỗi.' } } });
        await openLeave();
        await screen.findByText('Không kiểm tra được lịch hẹn bị ảnh hưởng');
        expect(screen.getByText('Dịch vụ xem trước đang lỗi.')).toBeInTheDocument();
        expect(screen.queryByText('Không có lịch hẹn nào của bệnh nhân bị ảnh hưởng.')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Không có lịch hẹn nào của bệnh nhân bị ảnh hưởng.');
        expect(doctorApi.previewLeave).toHaveBeenCalledTimes(2);
        expect(doctorApi.previewLeave).toHaveBeenLastCalledWith('2099-03-20', '2099-03-21');
        expect(screen.queryByText('Không kiểm tra được lịch hẹn bị ảnh hưởng')).not.toBeInTheDocument();
    });

    it('still permits leave submission when preview fails', async () => {
        vi.mocked(doctorApi.previewLeave).mockRejectedValue({ message: 'Xem trước bị lỗi.', status: 500 });
        await openLeave();
        await screen.findByText('Không kiểm tra được lịch hẹn bị ảnh hưởng');
        fireEvent.change(screen.getByLabelText('Lý do nghỉ phép'), { target: { value: 'Việc gia đình' } });
        const submit = screen.getByRole('button', { name: 'Gửi đơn nghỉ phép' });
        expect(submit).not.toBeDisabled();
        fireEvent.click(submit);
        await waitFor(() => expect(doctorApi.createLeaveRequest).toHaveBeenCalledExactlyOnceWith({
            startDate: '2099-03-20', endDate: '2099-03-21', reason: 'Việc gia đình'
        }));
    });

    it('ignores an old preview failure after the newly selected dates have loaded successfully', async () => {
        let rejectOld!: (error: unknown) => void;
        vi.mocked(doctorApi.previewLeave).mockImplementationOnce(() => new Promise((_, reject) => { rejectOld = reject; }));
        await openLeave();
        await waitFor(() => expect(doctorApi.previewLeave).toHaveBeenCalledTimes(1));
        setDate('Đến ngày', '22/03/2099');
        await screen.findByText('Không có lịch hẹn nào của bệnh nhân bị ảnh hưởng.');
        await act(async () => { rejectOld({ message: 'Lỗi của khoảng ngày cũ.', status: 500 }); });
        expect(screen.queryByText('Không kiểm tra được lịch hẹn bị ảnh hưởng')).not.toBeInTheDocument();
        expect(screen.getByText('Không có lịch hẹn nào của bệnh nhân bị ảnh hưởng.')).toBeInTheDocument();
        expect(doctorApi.previewLeave).toHaveBeenLastCalledWith('2099-03-20', '2099-03-22');
    });

    it('shows a prescription load error and retries instead of treating it as absent', async () => {
        const appointment = { id: 81, appointmentCode: 'APT-ERROR-81', patientId: 17, patientName: 'Người bệnh Test', patientPhone: '0901234567',
            patientGender: 'Female', patientDob: null, specialtyId: 3, appointmentDate: '2026-10-20', startTime: '09:00:00', endTime: '09:30:00', reason: null, status: 'Confirmed' };
        let prescriptionAttempts = 0;
        vi.mocked(axiosClient.get).mockImplementation(async url => {
            if (url.endsWith('/prescription')) {
                if (++prescriptionAttempts === 1) throw { message: 'Đường truyền đơn thuốc bị lỗi.', status: 503 };
                return ok({ id: 7, status: 'Issued', items: [{ medicineId: 3, medicineCode: 'MED03', medicineName: 'Thuốc tải lại', unit: 'viên', quantity: 10 }] });
            }
            if (url.endsWith('/history')) return ok([]);
            return ok({ items: [appointment], totalItems: 1 });
        });
        mount(<DoctorAppointments />);
        fireEvent.click(await screen.findByRole('button', { name: 'Chi tiết' }));
        await screen.findByText('Không tải được đơn thuốc');
        expect(screen.getByText('Đường truyền đơn thuốc bị lỗi.')).toBeInTheDocument();
        expect(screen.queryByText('Đơn thuốc đã kê')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Thuốc tải lại (MED03)');
        expect(prescriptionAttempts).toBe(2);
        expect(screen.queryByText('Không tải được đơn thuốc')).not.toBeInTheDocument();
    });

    it('blocks today and past dates in Vietnam even when the device calendar is a day behind', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-10-17T18:00:00Z')); // Vietnam is already October 18.
        mount(<DoctorSchedule />);
        await screen.findByText('Không có lịch làm việc cho tuần này');
        fireEvent.click(screen.getByRole('button', { name: 'Đăng ký nghỉ phép' }));
        fireEvent.mouseDown(screen.getByLabelText('Từ ngày'));
        fireEvent.click(screen.getByLabelText('Từ ngày'));
        await waitFor(() => expect(document.querySelector('td[title="2026-10-18"]')).toBeInTheDocument());
        expect(document.querySelector('td[title="2026-10-17"]')).toHaveClass('ant-picker-cell-disabled');
        expect(document.querySelector('td[title="2026-10-18"]')).toHaveClass('ant-picker-cell-disabled');
        expect(document.querySelector('td[title="2026-10-19"]')).not.toHaveClass('ant-picker-cell-disabled');
        setDate('Từ ngày', '21/10/2026');
        fireEvent.click(screen.getByLabelText('Đến ngày'));
        await waitFor(() => expect(document.querySelector('.ant-picker-dropdown:not(.ant-picker-dropdown-hidden) td[title="2026-10-20"]')).toBeInTheDocument());
        const popup = '.ant-picker-dropdown:not(.ant-picker-dropdown-hidden) ';
        expect(document.querySelector(`${popup}td[title="2026-10-20"]`)).toHaveClass('ant-picker-cell-disabled');
        expect(document.querySelector(`${popup}td[title="2026-10-21"]`)).not.toHaveClass('ant-picker-cell-disabled');
        expect(document.querySelector(`${popup}td[title="2026-10-22"]`)).not.toHaveClass('ant-picker-cell-disabled');
    });
});
