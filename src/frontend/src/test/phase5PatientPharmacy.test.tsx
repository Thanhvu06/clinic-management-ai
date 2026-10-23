import { readFileSync } from 'node:fs';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { DialogProvider } from '../contexts/DialogContext';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { PatientPrescriptions } from '../pages/patient/PatientPrescriptions';
import { PharmacyPrescriptions } from '../pages/pharmacy/PharmacyPrescriptions';
import { getPrescriptionStatusLabel } from '../utils/prescriptionStatusLabels';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

const wrap = (node: React.ReactNode) => render(
    <ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter><DialogProvider>{node}</DialogProvider></MemoryRouter></ConfigProvider>
);

beforeEach(() => vi.resetAllMocks());
afterEach(cleanup);

describe('GĐ5 mục 12: nhãn khối trong lịch hẹn bệnh nhân', () => {
    it('shows sentence-case secondary section labels instead of uppercase ones', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => url.startsWith('/appointments/my')
            ? { success: true, data: { items: [{ id: 1, appointmentCode: 'APT-501', doctorId: 2, doctorName: 'BS. An', specialtyId: 3, specialtyName: 'Nội khoa',
                appointmentSlotId: 4, appointmentDate: '2030-01-02', startTime: '08:00:00', endTime: '08:30:00', reason: 'Đau đầu', status: 'Confirmed' }] } }
            : { success: true, data: { items: [] } });
        wrap(<PatientAppointments />);
        const info = await screen.findByRole('heading', { level: 3, name: 'Thông tin khám' });
        const notes = screen.getByRole('heading', { level: 3, name: 'Triệu chứng / ghi chú' });
        expect(info.className).toMatch(/sectionTitle/);
        expect(notes.className).toBe(info.className);
        expect(screen.queryByText('THÔNG TIN KHÁM')).not.toBeInTheDocument();
        const css = readFileSync('src/pages/patient/PatientAppointments.module.css', 'utf8');
        expect(css).toMatch(/\.sectionTitle\s*\{[^}]*color:\s*var\(--cc-color-text-secondary\)/);
        expect(css).not.toMatch(/uppercase/);
    });
});

describe('GĐ5 mục 13: tiêu đề đơn thuốc của bệnh nhân', () => {
    it('uses the breadcrumb title without a repeated subtitle and keeps the Issued label', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: [{
            id: 1, code: 'RX-1', appointmentId: 1, appointmentCode: 'APT-1', appointmentDate: '2026-10-06', doctorName: 'BS. An', specialtyName: 'Nội khoa',
            diagnosis: 'Khám định kỳ', status: 'Issued', createdAt: '2026-10-06T08:00:00Z', priceIsReference: false, totalAmount: null, items: [],
        }] });
        wrap(<PatientPrescriptions />);
        const heading = await screen.findByRole('heading', { level: 1, name: 'Đơn thuốc của tôi' });
        expect(screen.queryByText('Đơn thuốc điện tử')).not.toBeInTheDocument();
        expect(screen.queryByText(/Xem lịch sử đơn thuốc được bác sĩ chỉ định/)).not.toBeInTheDocument();
        expect(heading.nextElementSibling?.tagName).not.toBe('P');
        expect(await screen.findByText('Đã phát hành')).toBeInTheDocument();
        expect(screen.queryByText('Chờ cấp thuốc')).not.toBeInTheDocument();
    });
});

describe('GĐ5 mục 14: một kiểu dấu bắt buộc', () => {
    it.each([
        'pages/patient/BookAppointment.tsx', 'pages/patient/PatientProfile.tsx', 'pages/patient/PatientAppointments.tsx',
        'pages/patient/PatientRevisit.tsx', 'pages/patient/PackageRegistration.tsx', 'pages/Login.tsx', 'pages/Register.tsx',
    ])('%s has no "(*)" label marker', path => {
        expect(readFileSync(`src/${path}`, 'utf8')).not.toContain('(*)');
    });

    it('marks the cancel reason with the single antd asterisk', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => url.startsWith('/appointments/my')
            ? { success: true, data: { items: [{ id: 1, appointmentCode: 'APT-502', doctorId: 2, doctorName: 'BS. An', specialtyId: 3, specialtyName: 'Nội khoa',
                appointmentSlotId: 4, appointmentDate: '2030-01-02', startTime: '08:00:00', endTime: '08:30:00', reason: null, status: 'Confirmed' }] } }
            : { success: true, data: { items: [] } });
        wrap(<PatientAppointments />);
        fireEvent.click(await screen.findByRole('button', { name: /Hủy lịch/ }));
        const label = await screen.findByText('Lý do hủy lịch');
        expect(label.closest('label')).toHaveClass('ant-form-item-required');
        expect(label.textContent).toBe('Lý do hủy lịch');
    });
});

describe('GĐ5 mục 15: nhãn đơn đã phát hành theo ngữ cảnh', () => {
    it.each([
        ['Issued', 'Đã phát hành', 'Chờ cấp thuốc'], ['Dispensed', 'Đã cấp phát', 'Đã cấp phát'], ['Draft', 'Nháp', 'Nháp'],
        ['Cancelled', 'Đã hủy', 'Đã hủy'], ['ReservedForPurchase', 'Giữ thuốc chờ mua', 'Giữ thuốc chờ mua'], ['Unknown', 'Không xác định', 'Không xác định'],
    ])('%s reads %s by default and %s for the pharmacist', (status, defaultLabel, pharmacyLabel) => {
        expect(getPrescriptionStatusLabel(status)).toBe(defaultLabel);
        expect(getPrescriptionStatusLabel(status, 'pharmacy')).toBe(pharmacyLabel);
    });

    it('shows Chờ cấp thuốc in the pharmacy list and detail modal', async () => {
        const row = { id: 10, appointmentId: 101, appointmentCode: 'APT-101', appointmentDate: '2026-09-07', patientName: 'Nguyễn Văn Test',
            patientPhone: '0901234567', doctorName: 'BS. Khải', status: 'Issued', itemCount: 1, createdAt: '2026-09-06T10:00:00Z' };
        vi.mocked(axiosClient.get)
            .mockResolvedValueOnce({ success: true, data: { items: [row], totalItems: 1 } })
            .mockResolvedValueOnce({ success: true, data: { ...row, patientId: 1, doctorId: 1, items: [{ medicineId: 1, medicineCode: 'MED01', medicineName: 'Paracetamol 500mg',
                unit: 'Viên', quantity: 10, availableStock: 100, isActive: true, dosage: '1 viên', frequency: '2 lần/ngày' }] } })
            .mockResolvedValueOnce({ success: true, data: { prescriptionId: 10, paymentStatus: 'paid', isFullyPaid: true, items: [] } });
        wrap(<PharmacyPrescriptions />);
        expect(await screen.findByText('Chờ cấp thuốc')).toBeInTheDocument();
        expect(screen.queryByText('Đã phát hành')).not.toBeInTheDocument();
        fireEvent.click(screen.getByText('Xem & cấp phát'));
        const dialog = await screen.findByRole('dialog');
        await waitFor(() => expect(within(dialog).getByText('Chờ cấp thuốc')).toBeInTheDocument());
        expect(within(dialog).queryByText('Đã phát hành')).not.toBeInTheDocument();
    });
});
