import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AdminMedicines } from '../pages/admin/AdminMedicines';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), delete: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn() }) }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });
const medicine = { id: 7, code: 'MED01', name: 'Paracetamol', unit: 'Viên', stockQuantity: 200, reorderLevel: 40, isActive: true, isPrescriptionRequired: false, categoryId: 2, categoryName: 'Giảm đau' };
beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
    vi.mocked(axiosClient.get).mockImplementation(async (url) => ({ success: true, data: String(url).includes('medicine-categories') ? [{ id: 2, name: 'Giảm đau', isActive: true }, { id: 3, name: 'Nhóm tắt', isActive: false }] : { items: [medicine], totalItems: 1 } }) as any);
    vi.mocked(axiosClient.post).mockResolvedValue({ success: true, data: { ...medicine, id: 8 } } as any);
    vi.mocked(axiosClient.put).mockResolvedValue({ success: true, data: medicine } as any);
});
it('shows category and prescription labels in the existing table', async () => {
    render(<AdminMedicines />);
    expect(await screen.findByText('Không kê đơn')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Nhóm' })).toBeInTheDocument();
    expect(within(screen.getByRole('table', { name: 'Danh mục thuốc' })).getByText('Giảm đau')).toBeInTheDocument();
});
it('sends all catalog fields on create, selects active categories and uploads the selected image', async () => {
    render(<AdminMedicines />);
    await screen.findByText('Paracetamol');
    fireEvent.click(screen.getByRole('button', { name: 'Thêm thuốc mới' }));
    fireEvent.change(screen.getByPlaceholderText('VD: PARA500'), { target: { value: 'NEW01' } });
    fireEvent.change(screen.getByPlaceholderText('VD: Paracetamol 500mg'), { target: { value: 'New medicine' } });
    for (const [label, value] of [['Hoạt chất', 'Paracetamol'], ['Hàm lượng', '500 mg'], ['Dạng bào chế', 'Viên nén'], ['Nhà sản xuất', 'Example'], ['Mô tả', 'Mô tả mẫu'], ['Hướng dẫn bảo quản', 'Khô ráo']])
        fireEvent.change(screen.getByLabelText(label), { target: { value } });
    fireEvent.change(screen.getByLabelText('Nhóm thuốc'), { target: { value: '2' } });
    expect(screen.queryByRole('option', { name: 'Nhóm tắt' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Thuốc kê đơn'));
    const file = new File(['png'], 'medicine.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Ảnh thuốc'), { target: { files: [file] } });
    fireEvent.click(screen.getByRole('button', { name: /^Thêm thuốc$/ }));
    await waitFor(() => expect(axiosClient.post).toHaveBeenCalledWith('/admin/medicines', expect.objectContaining({ activeIngredient: 'Paracetamol', strength: '500 mg', dosageForm: 'Viên nén', manufacturer: 'Example', categoryId: 2, isPrescriptionRequired: false, description: 'Mô tả mẫu', storageInstructions: 'Khô ráo' })));
    await waitFor(() => expect(axiosClient.post).toHaveBeenCalledWith('/admin/medicines/8/image', expect.any(FormData), expect.any(Object)));
    const upload = vi.mocked(axiosClient.post).mock.calls.find(call => call[0].endsWith('/image'))!;
    expect((upload[1] as FormData).get('file')).toBe(file);
});
it('sends extended fields on edit and deletes an existing image', async () => {
    vi.mocked(axiosClient.get).mockImplementation(async (url) => ({ success: true, data: String(url).includes('medicine-categories') ? [] : { items: [{ ...medicine, imageUrl: '/media/medicines/7.png', imagePath: '7.png', strength: '500 mg' }], totalItems: 1 } }) as any);
    vi.mocked(axiosClient.delete).mockResolvedValue({ success: true, data: medicine } as any);
    render(<AdminMedicines />);
    fireEvent.click(await screen.findByRole('button', { name: 'Sửa' }));
    fireEvent.change(screen.getByLabelText('Hàm lượng'), { target: { value: '250 mg' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xóa ảnh' }));
    await waitFor(() => expect(axiosClient.delete).toHaveBeenCalledWith('/admin/medicines/7/image'));
    fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
    await waitFor(() => expect(axiosClient.put).toHaveBeenCalledWith('/admin/medicines/7', expect.objectContaining({ strength: '250 mg', isPrescriptionRequired: false })));
});
