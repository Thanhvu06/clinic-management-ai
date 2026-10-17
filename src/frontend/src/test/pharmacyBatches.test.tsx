import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { PharmacyBatches } from '../pages/pharmacy/PharmacyBatches';
import type { MedicineBatch } from '../types/pharmacyBatch';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

const expired: MedicineBatch = {
    id: 7, medicineId: 3, medicineCode: 'MED03', medicineName: 'Thuốc kiểm tra', unit: 'viên', batchNumber: 'EXPIRED-7',
    expiryDate: '2020-03-01', quantityReceived: 20, quantityRemaining: 10, daysToExpiry: -1, status: 'Expired'
};
const listResponse = { success: true, data: { items: [expired], totalItems: 1, page: 1, pageSize: 20 } };
const show = () => render(<ConfigProvider theme={{ token: { motion: false } }}><PharmacyBatches /></ConfigProvider>);

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockImplementation(async (url) => url === '/medicines/active'
        ? { success: true, data: [{ id: 3, code: 'MED03', name: 'Thuốc kiểm tra', unit: 'viên' }] }
        : listResponse);
    vi.mocked(axiosClient.post).mockResolvedValue({ success: true, data: expired });
});
afterEach(cleanup);

describe('Medicine batch management', () => {
    it('loads batches and sends status/search filters to the API', async () => {
        show();
        await screen.findByText('EXPIRED-7');
        expect(screen.getByText('01/03/2020')).toBeInTheDocument();
        expect(screen.getByText('10 viên')).toBeInTheDocument();
        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Trạng thái lô' }));
        fireEvent.click(await screen.findByText('Sắp hết hạn (≤ 90 ngày)'));
        await waitFor(() => expect(axiosClient.get).toHaveBeenLastCalledWith('/pharmacy/batches', {
            params: { status: 'expiring', search: '', page: 1, pageSize: 20 }
        }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Tìm thuốc' }), { target: { value: ' MED03 ' } });
        await waitFor(() => expect(axiosClient.get).toHaveBeenLastCalledWith('/pharmacy/batches', {
            params: { status: 'expiring', search: 'MED03', page: 1, pageSize: 20 }
        }));
    });

    it('validates batch number, expiry and positive quantity before posting a trimmed import', async () => {
        show();
        await screen.findByText('EXPIRED-7');
        fireEvent.click(screen.getByRole('button', { name: 'Nhập lô mới' }));
        const dialog = within(await screen.findByRole('dialog'));
        const medicine = await dialog.findByLabelText('Thuốc');
        fireEvent.mouseDown(medicine);
        fireEvent.click(await screen.findByText('MED03 – Thuốc kiểm tra'));
        fireEvent.change(dialog.getByLabelText('Hạn dùng'), { target: { value: '2020-01-01' } });
        fireEvent.change(dialog.getByLabelText('Số lượng'), { target: { value: '0' } });
        fireEvent.click(dialog.getByRole('button', { name: 'Nhập lô' }));
        await dialog.findByText('Vui lòng nhập số lô.');
        await dialog.findByText('Hạn dùng phải sau ngày hôm nay.');
        await dialog.findByText('Số lượng phải là số nguyên lớn hơn 0.');
        expect(axiosClient.post).not.toHaveBeenCalled();
        fireEvent.change(dialog.getByLabelText('Số lô'), { target: { value: '  IMPORT-1  ' } });
        fireEvent.change(dialog.getByLabelText('Hạn dùng'), { target: { value: '2099-03-01' } });
        fireEvent.change(dialog.getByLabelText('Số lượng'), { target: { value: '25' } });
        fireEvent.change(dialog.getByLabelText('Giá nhập (tùy chọn)'), { target: { value: '123.45' } });
        fireEvent.click(dialog.getByRole('button', { name: 'Nhập lô' }));
        await waitFor(() => expect(axiosClient.post).toHaveBeenCalledExactlyOnceWith('/pharmacy/batches', {
            medicineId: 3, batchNumber: 'IMPORT-1', expiryDate: '2099-03-01', quantity: 25, unitCost: 123.45
        }));
        await screen.findByText('Nhập lô thuốc thành công.');
    });

    it('requires a trimmed disposal reason of at least five characters', async () => {
        show();
        fireEvent.click(await screen.findByRole('button', { name: 'Hủy lô' }));
        const dialog = within(await screen.findByRole('dialog'));
        fireEvent.click(dialog.getByRole('button', { name: 'Xác nhận hủy lô' }));
        await dialog.findByText('Vui lòng nhập lý do hủy lô.');
        fireEvent.change(dialog.getByLabelText('Lý do hủy lô'), { target: { value: '  abc  ' } });
        fireEvent.click(dialog.getByRole('button', { name: 'Xác nhận hủy lô' }));
        await dialog.findByText('Lý do hủy lô phải có ít nhất 5 ký tự.');
        expect(axiosClient.post).not.toHaveBeenCalled();
        fireEvent.change(dialog.getByLabelText('Lý do hủy lô'), { target: { value: '  Thuốc đã hết hạn  ' } });
        fireEvent.click(dialog.getByRole('button', { name: 'Xác nhận hủy lô' }));
        await waitFor(() => expect(axiosClient.post).toHaveBeenCalledExactlyOnceWith('/pharmacy/batches/7/dispose', { reason: 'Thuốc đã hết hạn' }));
        await screen.findByText('Hủy lô thuốc thành công.');
    });

    it('shows the API error with retry and then a separate empty state', async () => {
        vi.mocked(axiosClient.get).mockRejectedValueOnce({ response: { data: { message: 'Không kết nối được kho thuốc.' } } });
        show();
        await screen.findByText('Không kết nối được kho thuốc.');
        expect(screen.queryByText('Không có lô thuốc phù hợp.')).not.toBeInTheDocument();
        vi.mocked(axiosClient.get).mockResolvedValueOnce({ success: true, data: { items: [], totalItems: 0 } });
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Không có lô thuốc phù hợp.');
        expect(axiosClient.get).toHaveBeenCalledTimes(2);
    });

    it('retries a failed import without closing or losing the entered values', async () => {
        vi.mocked(axiosClient.post).mockRejectedValueOnce({ response: { data: { message: 'Lô này đã tồn tại.' } } });
        show();
        await screen.findByText('EXPIRED-7');
        fireEvent.click(screen.getByRole('button', { name: 'Nhập lô mới' }));
        const dialog = within(await screen.findByRole('dialog'));
        fireEvent.mouseDown(await dialog.findByLabelText('Thuốc'));
        fireEvent.click(await screen.findByText('MED03 – Thuốc kiểm tra'));
        fireEvent.change(dialog.getByLabelText('Số lô'), { target: { value: 'RETRY-1' } });
        fireEvent.change(dialog.getByLabelText('Hạn dùng'), { target: { value: '2099-03-01' } });
        fireEvent.change(dialog.getByLabelText('Số lượng'), { target: { value: '5' } });
        fireEvent.click(dialog.getByRole('button', { name: 'Nhập lô' }));
        await dialog.findByText('Lô này đã tồn tại.');
        expect(dialog.getByLabelText('Số lô')).toHaveValue('RETRY-1');
        fireEvent.click(dialog.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Nhập lô thuốc thành công.');
        expect(axiosClient.post).toHaveBeenCalledTimes(2);
        expect(axiosClient.post).toHaveBeenLastCalledWith('/pharmacy/batches', {
            medicineId: 3, batchNumber: 'RETRY-1', expiryDate: '2099-03-01', quantity: 5, unitCost: null
        });
    });
});
