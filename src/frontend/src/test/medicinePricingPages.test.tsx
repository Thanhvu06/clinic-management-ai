import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { PharmacyInventory } from '../pages/pharmacy/PharmacyInventory';
import { MedicinePrices } from '../pages/public/MedicinePrices';
import { MedicineCard } from '../components/medicines/MedicineCard';
import { DialogProvider } from '../contexts/DialogContext';
import { medicineApi } from '../api/medicineApi';
import axiosClient from '../api/axiosClient';
import type { PublicMedicineDto } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../api/medicineApi', async importOriginal => ({
    ...await importOriginal<typeof import('../api/medicineApi')>(),
    medicineApi: { getPublicMedicines: vi.fn(), getPublicCategories: vi.fn() },
}));

const medicine: PublicMedicineDto = {
    id: 1, code: 'MED-ONE', name: 'Thuốc một', unit: 'Viên', strength: '500 mg', activeIngredient: 'Paracetamol',
    isPrescriptionRequired: true, unitPrice: 1500, availability: 'low',
};

describe('Medicine pricing pages', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.stubGlobal('ResizeObserver', class {
            observe() {}
            unobserve() {}
            disconnect() {}
        });
        vi.mocked(medicineApi.getPublicCategories).mockResolvedValue({ success: true, message: '', data: [{ id: 1, name: 'Giảm đau', sortOrder: 1 }] });
        vi.mocked(medicineApi.getPublicMedicines).mockResolvedValue({ success: true, message: '', data: { items: [medicine], page: 1, pageSize: 12, totalItems: 24, totalPages: 2 } });
    });
    afterEach(() => vi.unstubAllGlobals());

    it('defaults to the inventory grid and applies search to the preserved transaction table', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: String(url) === '/medicines/active' ? [
            { ...medicine, stockQuantity: 0 },
            { ...medicine, id: 2, code: 'MED-TWO', name: 'Thuốc hai', stockQuantity: 10, unitPrice: null },
        ] : { totalItems: 2, items: [
            { id: 1, medicineCode: 'MED-ONE', medicineName: 'Thuốc một', unit: 'Viên', type: 'StockIn', quantityChange: 5, balanceAfter: 5, createdAt: '2026-10-06T08:00:00Z', actorName: 'Dược sĩ' },
            { id: 2, medicineCode: 'MED-TWO', medicineName: 'Thuốc hai', unit: 'Viên', type: 'StockIn', quantityChange: 10, balanceAfter: 10, createdAt: '2026-10-06T08:00:00Z', actorName: 'Dược sĩ' },
        ] } }));
        render(<DialogProvider><PharmacyInventory /></DialogProvider>);

        const grid = screen.getByRole('region', { name: 'Lưới thuốc trong kho' });
        expect(await within(grid).findByText('Thuốc một')).toBeInTheDocument();
        expect(within(grid).getByText(/1\.500\s*₫/)).toBeInTheDocument();
        expect(within(grid).getByText('Chưa có giá')).toBeInTheDocument();
        expect(within(grid).getByText('Hết hàng')).toBeInTheDocument();
        expect(within(grid).getAllByRole('img', { name: 'Chưa có ảnh thuốc' })).toHaveLength(2);

        fireEvent.change(screen.getByLabelText('Tìm thuốc trong kho'), { target: { value: 'MED-ONE' } });
        expect(within(grid).queryByText('Thuốc hai')).not.toBeInTheDocument();
        fireEvent.click(screen.getByText('Bảng'));
        const table = screen.getByRole('table');
        expect(within(table).getByText('Thời gian')).toBeInTheDocument();
        expect(within(table).getByText('Thuốc một')).toBeInTheDocument();
        expect(within(table).queryByText('Thuốc hai')).not.toBeInTheDocument();
    });

    it('loads public prices and availability, debounces search and sends category, rx and page filters', async () => {
        render(<MedicinePrices />);
        expect(screen.getByText('Đang tải bảng giá thuốc...')).toBeInTheDocument();
        expect(await screen.findByText('Thuốc một')).toBeInTheDocument();
        expect(screen.getByText('Sắp hết')).toBeInTheDocument();
        expect(screen.getByText('Thuốc kê đơn — chỉ bán theo đơn của bác sĩ.')).toBeInTheDocument();
        expect(screen.queryByText(/Tồn kho:/)).not.toBeInTheDocument();

        fireEvent.change(screen.getByLabelText('Tìm theo tên hoặc hoạt chất'), { target: { value: 'Paracetamol' } });
        expect(medicineApi.getPublicMedicines).toHaveBeenCalledTimes(1);
        await waitFor(() => expect(medicineApi.getPublicMedicines).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'Paracetamol', page: 1 })));
        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Danh mục thuốc' }));
        fireEvent.click(screen.getByText('Giảm đau'));
        await waitFor(() => expect(medicineApi.getPublicMedicines).toHaveBeenLastCalledWith(expect.objectContaining({ categoryId: 1 })));
        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Loại thuốc' }));
        fireEvent.click(screen.getByText('Thuốc kê đơn', { selector: '.ant-select-item-option-content' }));
        await waitFor(() => expect(medicineApi.getPublicMedicines).toHaveBeenLastCalledWith(expect.objectContaining({ type: 'rx' })));
        await screen.findByText('Thuốc một');
        fireEvent.click(screen.getByTitle('2'));
        await waitFor(() => expect(medicineApi.getPublicMedicines).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })));
    });

    it('shows friendly public errors, retries and then displays the empty state', async () => {
        vi.mocked(medicineApi.getPublicMedicines).mockRejectedValueOnce(new Error('ECONNRESET technical detail'));
        render(<MedicinePrices />);
        expect(await screen.findByText('Chưa thể tải bảng giá thuốc. Bạn vui lòng thử lại.')).toBeInTheDocument();
        expect(screen.queryByText(/ECONNRESET/)).not.toBeInTheDocument();
        vi.mocked(medicineApi.getPublicMedicines).mockResolvedValue({ success: true, message: '', data: { items: [], page: 1, pageSize: 12, totalItems: 0, totalPages: 0 } });
        fireEvent.click(screen.getByRole('button', { name: /Thử lại/ }));
        expect(await screen.findByText('Chưa tìm thấy thuốc phù hợp')).toBeInTheDocument();
    });

    it('replaces a failed medicine image with a pill placeholder', () => {
        render(<MedicineCard medicine={{ ...medicine, imageUrl: '/uploads/medicine.png' }} publicView />);
        const image = screen.getByRole('img', { name: 'Thuốc một' });
        expect(image).toHaveAttribute('src', 'http://localhost:5258/uploads/medicine.png');
        fireEvent.error(image);
        expect(screen.getByRole('img', { name: 'Chưa có ảnh thuốc' })).toBeInTheDocument();
        expect(screen.queryByRole('img', { name: 'Thuốc một' })).not.toBeInTheDocument();
    });
});
