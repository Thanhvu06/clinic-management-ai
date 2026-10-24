import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { getGenderLabel } from '../utils/genderLabels';
import { getStockTransactionLabel, stockTransactionLabels } from '../utils/stockTransactionLabels';
import { formatPrescriptionCode, formatPrescriptionReference } from '../utils/prescriptionCode';
import { formatDateOnly, formatDateTime, formatDisplayDate } from '../utils/formatters';
import { Forbidden } from '../pages/Forbidden';
import { ForbiddenPage, NotFoundPage } from '../pages/Dashboards';

const auth = vi.hoisted(() => ({ user: null as { role: string } | null }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => auth }));

describe('Staff display formatting', () => {
    it.each([['Male', 'Nam'], ['Female', 'Nữ'], ['Other', 'Khác'], [0, 'Nam'], [1, 'Nữ'], [2, 'Khác'], [null, 'Chưa cập nhật']])('labels gender %s', (gender, label) => {
        expect(getGenderLabel(gender)).toBe(label);
    });
    it('labels all seven inventory transaction enums and the initial stock', () => {
        expect(Object.keys(stockTransactionLabels)).toHaveLength(7);
        expect(getStockTransactionLabel('Initial')).toBe('Tồn đầu kỳ');
        Object.entries(stockTransactionLabels).forEach(([name, label], index) => {
            expect(getStockTransactionLabel(name)).toBe(label);
            expect(getStockTransactionLabel(index + 1)).toBe(label);
        });
    });
    it('uses an existing prescription code or the prescribed date and padded id', () => {
        expect(formatPrescriptionCode({ id: 1, createdAt: '2026-10-24T09:00:00', code: 'RX-EXISTING' })).toBe('RX-EXISTING');
        expect(formatPrescriptionCode({ id: 1, createdAt: '2026-10-24T09:00:00' })).toBe('RX-261024-0001');
        expect(formatPrescriptionCode({ id: 12345, createdAt: '2026-01-02' })).toBe('RX-260102-12345');
        expect(formatPrescriptionReference({ id: 1, createdAt: '2026-10-24' }, 'Cấp phát thuốc cho đơn #1')).toBe('Cấp phát thuốc cho đơn RX-261024-0001');
        expect(formatPrescriptionReference({ id: 1, createdAt: '2026-10-24' }, 'Không thay số #12')).toBe('Không thay số #12');
    });
    it.each([null, undefined, '', '0001-01-01T00:00:00', '1900-12-31'])('hides empty or default date %s', value => {
        expect(formatDateOnly(value)).toBe('—');
        expect(formatDisplayDate(value)).toBe('—');
        expect(formatDateTime(value)).toBe('—');
    });
    it('displays a four-digit year and minute precision', () => {
        expect(formatDateOnly('2026-10-24')).toBe('24/10/2026');
        expect(formatDateTime(new Date(2026, 9, 24, 9, 5, 59))).toBe('24/10/2026 09:05');
    });
    it.each([Forbidden, ForbiddenPage, NotFoundPage])('uses a primary button and sends staff home', Component => {
        auth.user = { role: 'Doctor' };
        render(<MemoryRouter initialEntries={['/error']}><Routes><Route path="/error" element={<Component />} /><Route path="/doctor" element={<p>Bàn làm việc đích</p>} /></Routes></MemoryRouter>);
        const button = screen.getByRole('button', { name: /Quay (lại|về) trang chủ/ });
        expect(button).toHaveClass('ant-btn-primary');
        fireEvent.click(button);
        expect(screen.getByText('Bàn làm việc đích')).toBeInTheDocument();
    });
    it.each([['Admin', '/admin'], ['Receptionist', '/reception'], ['Pharmacist', '/pharmacy'], ['DiagnosticTechnician', '/diagnostics'], ['LabSupervisor', '/lab']])('returns %s to the correct dashboard', (role, path) => {
        auth.user = { role };
        render(<MemoryRouter initialEntries={['/error']}><Routes><Route path="/error" element={<NotFoundPage />} /><Route path={path} element={<p>Đã về bàn làm việc</p>} /></Routes></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Quay lại trang chủ/ }));
        expect(screen.getByText('Đã về bàn làm việc')).toBeInTheDocument();
    });
});
