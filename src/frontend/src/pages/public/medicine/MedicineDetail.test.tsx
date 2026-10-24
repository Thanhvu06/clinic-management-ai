import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { medicineApi } from '../../../api/medicineApi';
import { getPublicLocations } from '../../../api/publicLocationsApi';
import type { PublicMedicineDto } from '../../../types';
import { MedicineDetail } from '../MedicineDetail';

vi.mock('../../../api/medicineApi', () => ({ medicineApi: { getPublicMedicine: vi.fn(), getPublicCategories: vi.fn(), getPublicMedicines: vi.fn() }, medicineImageUrl: (path: string) => path }));
vi.mock('../../../api/publicLocationsApi', () => ({ getPublicLocations: vi.fn() }));
const medicine: PublicMedicineDto = { id: 23, name: 'Thuốc mẫu', code: 'MED23', unit: 'Viên', categoryName: 'Nhóm mẫu', isPrescriptionRequired: true, availability: 'in_stock', unitPrice: 1500, description: 'Mô tả thuốc mẫu', indications: 'Chỉ định mẫu', precautions: '  ' };
function mount() {
    return render(<MemoryRouter initialEntries={['/medicines/23']}><Routes><Route path="/medicines/:id" element={<MedicineDetail />} /></Routes></MemoryRouter>);
}
beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(medicineApi.getPublicMedicine).mockResolvedValue({ success: true, message: '', data: medicine });
    vi.mocked(medicineApi.getPublicCategories).mockResolvedValue({ success: true, message: '', data: [{ id: 7, name: 'Nhóm mẫu', sortOrder: 1 }] });
    vi.mocked(medicineApi.getPublicMedicines).mockResolvedValue({ success: true, message: '', data: { items: [medicine, ...Array.from({ length: 7 }, (_, i) => ({ ...medicine, id: 100 + i, name: `Thuốc liên quan ${i}` }))], page: 1, pageSize: 6, totalItems: 8, totalPages: 2 } });
    vi.mocked(getPublicLocations).mockResolvedValue({ success: true, data: [{ id: 1, code: 'CS', name: 'Cơ sở mẫu', address: 'Địa chỉ nhà thuốc', city: '', openingHours: '08:00–17:00', phone: '0123 456', features: [], isActive: true }] });
});
afterEach(cleanup);

describe('Medicine article page', () => {
    it('shows prescription guidance, price per unit and the booking destination', async () => {
        mount();
        expect(await screen.findByText('Thuốc kê đơn')).toBeInTheDocument();
        expect(screen.getByText('Thuốc này chỉ được bán theo đơn của bác sĩ. Vui lòng khám để được kê đơn.')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Đặt lịch khám' })).toHaveAttribute('href', '/patient/book');
        expect(screen.getByText('1.500 đ')).toBeInTheDocument();
        expect(screen.queryByText('Có bán tại nhà thuốc phòng khám')).not.toBeInTheDocument();
        expect(getPublicLocations).not.toHaveBeenCalled();
        expect(screen.getByRole('navigation', { name: 'Đường dẫn trang' })).toHaveTextContent('Trang chủ/Bảng giá thuốc/Nhóm mẫu/Thuốc mẫu');
    });
    it('shows OTC guidance with actual location contacts and no purchase button', async () => {
        vi.mocked(medicineApi.getPublicMedicine).mockResolvedValue({ success: true, message: '', data: { ...medicine, isPrescriptionRequired: false } });
        mount();
        expect(await screen.findByText('Không kê đơn')).toBeInTheDocument();
        expect(await screen.findByText('Địa chỉ nhà thuốc')).toBeInTheDocument();
        expect(screen.getByText('Có bán tại nhà thuốc phòng khám')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: '0123 456' })).toHaveAttribute('href', 'tel:0123456');
        expect(screen.queryByRole('link', { name: 'Đặt lịch khám' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Mua|giỏ hàng/ })).not.toBeInTheDocument();
    });
    it('includes only populated fields in the TOC, and anchors point to visible article sections', async () => {
        mount();
        const toc = await screen.findByRole('navigation', { name: 'Mục lục thông tin thuốc' });
        expect(within(toc).getAllByRole('link').map(link => link.textContent)).toEqual(['Mô tả', 'Chỉ định']);
        expect(screen.queryByRole('heading', { name: 'Thận trọng' })).not.toBeInTheDocument();
        const link = within(toc).getByRole('link', { name: 'Chỉ định' });
        expect(document.querySelector(link.getAttribute('href')!)).toHaveTextContent('Chỉ định mẫu');
        fireEvent.click(link);
        expect(link).toHaveAttribute('aria-current', 'location');
        expect(screen.getByText('Chỉ định mẫu')).toBeVisible();
    });
    it('tracks the viewed section while scrolling', async () => {
        mount();
        const toc = await screen.findByRole('navigation', { name: 'Mục lục thông tin thuốc' });
        const section = document.getElementById('medicine-indications')!;
        const bounds = vi.spyOn(section, 'getBoundingClientRect').mockReturnValue({ top: 300 } as DOMRect);
        fireEvent.scroll(window);
        expect(within(toc).getByRole('link', { name: 'Mô tả' })).toHaveAttribute('aria-current', 'location');
        bounds.mockReturnValue({ top: 100 } as DOMRect);
        fireEvent.scroll(window);
        expect(within(toc).getByRole('link', { name: 'Chỉ định' })).toHaveAttribute('aria-current', 'location');
        bounds.mockRestore();
    });
    it('queries the resolved category id and excludes the current medicine, with at most five cards', async () => {
        mount();
        const region = await screen.findByRole('region', { name: 'Thuốc cùng nhóm' });
        await within(region).findByRole('heading', { name: 'Thuốc liên quan 0' });
        expect(medicineApi.getPublicMedicines).toHaveBeenCalledWith({ categoryId: 7, pageSize: 6 });
        expect(within(region).getAllByRole('heading', { level: 3 })).toHaveLength(5);
        expect(within(region).queryByRole('link', { name: /^Thuốc mẫu/ })).not.toBeInTheDocument();
        expect(within(region).getByRole('link', { name: /Thuốc liên quan 0/ })).toHaveAttribute('href', '/medicines/100');
    });
    it('hides related items when the category has no other medicine', async () => {
        vi.mocked(medicineApi.getPublicMedicines).mockResolvedValue({ success: true, message: '', data: { items: [medicine], page: 1, pageSize: 6, totalItems: 1, totalPages: 1 } });
        mount();
        await screen.findByRole('heading', { name: medicine.name });
        await waitFor(() => expect(screen.queryByRole('region', { name: 'Thuốc cùng nhóm' })).not.toBeInTheDocument());
    });
    it('isolates a related-items failure and retries without reloading the medicine', async () => {
        vi.mocked(medicineApi.getPublicMedicines).mockRejectedValueOnce({ status: 503, message: 'Lỗi thuốc cùng nhóm' });
        mount();
        const error = await screen.findByRole('alert');
        expect(screen.getByText('Chỉ định mẫu')).toBeVisible();
        fireEvent.click(within(error).getByRole('button', { name: 'Thử lại' }));
        await screen.findByRole('heading', { name: 'Thuốc liên quan 0' });
        expect(medicineApi.getPublicMedicine).toHaveBeenCalledTimes(1);
    });
    it('keeps storage guidance in the article only, not in the spec table', async () => {
        vi.mocked(medicineApi.getPublicMedicine).mockResolvedValue({ success: true, message: '', data: { ...medicine, storageInstructions: 'Nơi khô ráo' } });
        const { container } = mount();
        await screen.findByRole('heading', { name: medicine.name });
        const terms = Array.from(container.querySelectorAll('dl dt')).map(item => item.textContent);
        expect(terms).toEqual(['Mã thuốc', 'Hoạt chất', 'Hàm lượng', 'Dạng bào chế', 'Đơn vị', 'Nhóm']);
        expect(screen.getByRole('heading', { name: 'Bảo quản' })).toBeInTheDocument();
        expect(screen.getAllByText('Nơi khô ráo')).toHaveLength(1);
    });
});
