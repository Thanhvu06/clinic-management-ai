import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ExpiryBadge } from '../components/common/ExpiryBadge';
import { OnlineBookingNotice } from '../pages/patient/OnlineBookingNotice';
import { getPrescriptionStatusLabel } from '../utils/prescriptionStatusLabels';
import { getAppointmentHistoryActionLabel } from '../utils/appointmentHistoryLabels';
import { getVisitStatusLabel } from '../utils/visitStatusLabels';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));

describe('Quy tắc hết hạn', () => {
    beforeEach(() => { vi.clearAllMocks(); });
    it('hiện cờ tính toán và ẩn cờ khi dữ liệu còn hạn', () => {
        const { rerender } = render(<ExpiryBadge overdue completion />);
        expect(screen.getByText('Quá hạn hoàn tất')).toBeInTheDocument();
        rerender(<ExpiryBadge overdue />);
        expect(screen.getByText('Quá hạn')).toBeInTheDocument();
        rerender(<ExpiryBadge overdue={false} />);
        expect(screen.queryByText('Quá hạn')).not.toBeInTheDocument();
    });
    it('dịch trạng thái hết hạn, lịch sử và vắng mặt', () => {
        expect(getPrescriptionStatusLabel('Expired')).toBe('Hết hạn, cần kê lại');
        expect(getPrescriptionStatusLabel('Expired', 'pharmacy')).toBe('Hết hạn, cần kê lại');
        expect(getAppointmentHistoryActionLabel('RequestExpired')).toBe('Hết hạn xử lý');
        expect(getVisitStatusLabel('Expired')).toBe('Hết hạn');
        expect(getVisitStatusLabel('NoShow')).toContain('Vắng');
    });
    it('hiện lý do khóa online từ API và hướng dẫn đến lễ tân', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue({ data: { isBlocked: true, message: 'Tạm khóa đặt lịch online. Vui lòng liên hệ lễ tân.' } });
        render(<OnlineBookingNotice />);
        expect(await screen.findByText('Tạm khóa đặt lịch online. Vui lòng liên hệ lễ tân.')).toBeInTheDocument();
    });
    it('cho phép thử lại lỗi tải điều kiện đặt lịch', async () => {
        vi.mocked(axiosClient.get).mockRejectedValueOnce(new Error('Lỗi kết nối'))
            .mockResolvedValueOnce({ data: { isBlocked: false } });
        render(<OnlineBookingNotice />);
        fireEvent.click(await screen.findByRole('button', { name: /Thử lại/ }));
        await waitFor(() => expect(screen.queryByRole('button', { name: /Thử lại/ })).not.toBeInTheDocument());
        expect(axiosClient.get).toHaveBeenCalledTimes(2);
    });
});
