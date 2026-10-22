import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';
import { PackageRegistration } from '../pages/patient/PackageRegistration';
import { DoctorAppointmentDetail } from '../pages/doctor/DoctorAppointmentDetail';
import type { PatientClinicalContextDto } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getPatientClinicalContext: vi.fn(), getAppointmentHistory: vi.fn() } }));
const ok = <T,>(data: T) => ({ success: true, message: '', data });
const clinicalContext: PatientClinicalContextDto = {
    patientId: 1, patientName: 'Bệnh nhân chi tiết', patientPhone: '0901234567', patientGender: 'Female',
    patientDob: '1990-02-03', address: '12 Nguyễn Huệ', totalPastVisits: 1,
    currentAppointment: { id: 81, appointmentCode: 'APT-DETAIL-81', patientId: 1, doctorId: 2, doctorName: 'Bác sĩ hiện tại',
        specialtyId: 3, specialtyName: 'Nội khoa', appointmentDate: '2026-10-20', startTime: '09:00:00', endTime: '09:30:00', reason: 'Đau đầu kéo dài', status: 'Confirmed' },
    pastVisits: [{ appointmentId: 73, appointmentCode: 'APT-PAST-73', date: '2026-09-02', doctorName: 'Bác sĩ lần trước', specialtyName: 'Tim mạch', diagnosis: 'Tăng huyết áp', prescriptionItemNames: ['Amlodipine', 'Losartan'] }]
};
beforeEach(() => {
    vi.resetAllMocks();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-16T03:00:00Z'));
    vi.mocked(axiosClient.get).mockResolvedValue(ok({ id: 1, code: 'PKG', name: 'Gói thử điện thoại', price: 200000, includedServices: [], isActive: true }));
    vi.mocked(doctorApi.getPatientClinicalContext).mockResolvedValue(ok(structuredClone(clinicalContext)));
    vi.mocked(doctorApi.getAppointmentHistory).mockResolvedValue(ok([]));
});
afterEach(() => { cleanup(); vi.useRealTimers(); });
const mount = (page: React.ReactNode, route: string, pattern: string) => render(
    <ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[route]}><Routes><Route path={pattern} element={page} /></Routes></MemoryRouter></ConfigProvider>
);
const mountDetail = () => mount(<DoctorAppointmentDetail />, '/doctor/appointments/81', '/doctor/appointments/:id');

describe('Package phone rule', () => {
    it.each(['09012345678', '0201234567', '0|12345678'])('blocks invalid phone %s before confirmation or submission', async phone => {
        mount(<PackageRegistration />, '/patient/health-packages/1/register', '/patient/health-packages/:id/register');
        fireEvent.click(await screen.findByRole('button', { name: 'Tiếp tục bước 2' }));
        fireEvent.change(screen.getByLabelText('Ngày khám mong muốn'), { target: { value: '2026-10-20' } });
        fireEvent.change(screen.getByLabelText('Số điện thoại liên hệ'), { target: { value: phone } });
        fireEvent.click(screen.getByRole('button', { name: 'Xem lại thông tin' }));
        expect(await screen.findByText('Số điện thoại không hợp lệ (10 chữ số, bắt đầu bằng 03/05/07/08/09).')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Xác nhận thông tin đăng ký' })).not.toBeInTheDocument();
        expect(axiosClient.post).not.toHaveBeenCalled();
    });
    it.each(['03 1234 5678', '0512345678', '0712345678', '0812345678', '0912345678'])('accepts valid prefix and existing whitespace handling for %s', async phone => {
        mount(<PackageRegistration />, '/patient/health-packages/1/register', '/patient/health-packages/:id/register');
        fireEvent.click(await screen.findByRole('button', { name: 'Tiếp tục bước 2' }));
        fireEvent.change(screen.getByLabelText('Ngày khám mong muốn'), { target: { value: '2026-10-20' } });
        fireEvent.change(screen.getByLabelText('Số điện thoại liên hệ'), { target: { value: phone } });
        fireEvent.click(screen.getByRole('button', { name: 'Xem lại thông tin' }));
        expect(await screen.findByRole('heading', { name: 'Xác nhận thông tin đăng ký' })).toBeInTheDocument();
    });
});

describe('Doctor appointment detail', () => {
    it('formats date-only fields, shows appointment context and links past visits with diagnosis and medicines', async () => {
        mountDetail();
        await screen.findByText('Bệnh nhân chi tiết');
        expect(screen.getByText(/03\/02\/1990/)).toBeInTheDocument();
        expect(screen.getByText(/Ngày khám: 20\/10\/2026/)).toBeInTheDocument();
        expect(screen.getByText('Chuyên khoa: Nội khoa')).toBeInTheDocument();
        expect(screen.getByText(/Đau đầu kéo dài/)).toBeInTheDocument();
        expect(screen.getByText('Địa chỉ: 12 Nguyễn Huệ')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Lần khám trước (1)' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: '02/09/2026 · APT-PAST-73' })).toHaveAttribute('href', '/doctor/appointments/73');
        expect(screen.getByText('Bác sĩ: Bác sĩ lần trước')).toBeInTheDocument();
        expect(screen.getByText('Chuyên khoa: Tim mạch')).toBeInTheDocument();
        expect(screen.getByText(/Tăng huyết áp/)).toBeInTheDocument();
        expect(screen.getByText(/Amlodipine, Losartan/)).toBeInTheDocument();
    });
    it('localizes every supported history action and old/new statuses', async () => {
        const labels = { Created: 'Tạo mới', StatusChanged: 'Cập nhật trạng thái', Cancelled: 'Hủy lịch', Rescheduled: 'Đổi lịch', Confirmed: 'Xác nhận', Completed: 'Hoàn thành', NoShow: 'Đánh dấu vắng', RescheduleRequested: 'Yêu cầu đổi lịch', CancelRequested: 'Yêu cầu hủy' };
        vi.mocked(doctorApi.getAppointmentHistory).mockResolvedValue(ok(Object.keys(labels).map((action, id) => ({ id, action, createdAt: '2026-10-16T03:00:00Z', oldStatus: 'Confirmed', newStatus: 'Cancelled' }))));
        mountDetail();
        await screen.findByRole('heading', { name: 'Nhật ký lịch hẹn' });
        for (const [raw, label] of Object.entries(labels)) {
            expect(screen.getByText(label)).toBeInTheDocument();
            expect(screen.queryByText(raw)).not.toBeInTheDocument();
        }
        expect(screen.getAllByText('Từ Chờ tiếp nhận → Đã hủy')).toHaveLength(9);
    });
    it('hides null optional context and renders empty past visits', async () => {
        const context = structuredClone(clinicalContext);
        context.address = null; context.currentAppointment.reason = null; context.currentAppointment.specialtyName = ''; context.pastVisits = []; context.totalPastVisits = 0;
        vi.mocked(doctorApi.getPatientClinicalContext).mockResolvedValue(ok(context));
        mountDetail();
        await screen.findByText('Chưa có lần khám trước.');
        expect(screen.queryByText(/Địa chỉ:/)).not.toBeInTheDocument();
        expect(screen.queryByText('Lý do khám:')).not.toBeInTheDocument();
        expect(screen.queryByText(/Chuyên khoa:/)).not.toBeInTheDocument();
    });
    it.each(['context', 'history'])('shows normalized server error and retries loadData when %s fails', async source => {
        const api = source === 'context' ? doctorApi.getPatientClinicalContext : doctorApi.getAppointmentHistory;
        vi.mocked(api).mockRejectedValueOnce({ status: 500, message: 'Máy chủ đang gián đoạn' });
        mountDetail();
        await screen.findByText('Máy chủ đang gián đoạn');
        expect(screen.queryByText('Không tìm thấy lịch khám')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Bệnh nhân chi tiết');
        expect(doctorApi.getPatientClinicalContext).toHaveBeenNthCalledWith(2, 81);
        expect(doctorApi.getAppointmentHistory).toHaveBeenNthCalledWith(2, 81);
    });
    it.each(['404', 'unsuccessful'])('shows not found only for %s response', async scenario => {
        if (scenario === '404') vi.mocked(doctorApi.getPatientClinicalContext).mockRejectedValueOnce({ status: 404, message: 'Missing' });
        else vi.mocked(doctorApi.getPatientClinicalContext).mockResolvedValueOnce({ success: false, message: 'Missing' });
        mountDetail();
        await screen.findByText('Không tìm thấy lịch khám');
        expect(screen.queryByRole('button', { name: 'Thử lại' })).not.toBeInTheDocument();
    });
    it('keeps retry busy state separate from the error', async () => {
        vi.mocked(doctorApi.getPatientClinicalContext).mockRejectedValueOnce({ message: 'Mất kết nối' });
        mountDetail();
        await screen.findByText('Mất kết nối');
        let resolve!: (value: ReturnType<typeof ok<PatientClinicalContextDto>>) => void;
        vi.mocked(doctorApi.getPatientClinicalContext).mockReturnValueOnce(new Promise(value => { resolve = value; }));
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Đang tải chi tiết hồ sơ...');
        expect(screen.queryByRole('button', { name: 'Thử lại' })).not.toBeInTheDocument();
        resolve(ok(clinicalContext));
        await waitFor(() => expect(screen.getByText('Bệnh nhân chi tiết')).toBeInTheDocument());
    });
});
