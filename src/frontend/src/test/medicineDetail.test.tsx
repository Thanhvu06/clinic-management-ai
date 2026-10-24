import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { MedicineDetail } from '../pages/public/MedicineDetail';
import { MedicineCard } from '../components/medicines/MedicineCard';
import { MedicinePrices } from '../pages/public/MedicinePrices';
import { PharmacyInventory } from '../pages/pharmacy/PharmacyInventory';
import { PatientPrescriptions } from '../pages/patient/PatientPrescriptions';
import { AdminMedicines } from '../pages/admin/AdminMedicines';
import type { PublicMedicineDto } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn() }) }));
const medicine: PublicMedicineDto = {
    id: 501, name: 'Thuốc chi tiết', code: 'DETAIL501', unit: 'Viên', activeIngredient: 'Ciprofloxacin', strength: '500 mg',
    dosageForm: 'Viên nén', categoryName: 'Kháng sinh', availability: 'in_stock', isPrescriptionRequired: true, unitPrice: 6000,
    indications: 'Chỉ định thử nghiệm', dosageAndAdministration: 'Dùng theo đơn của bác sĩ', contraindications: null,
    sideEffects: 'Tác dụng phụ mẫu', precautions: 'Thận trọng mẫu', overdoseManagement: 'Xử trí mẫu', storageInstructions: 'Bảo quản mẫu', description: 'Mô tả mẫu', interactions: [],
};
const labels = ['Mô tả', 'Chỉ định', 'Liều dùng và cách dùng', 'Tác dụng không mong muốn', 'Thận trọng', 'Xử trí quá liều', 'Bảo quản'];
const monographFields = [
    ['indications', 'Chỉ định', 2000], ['dosageAndAdministration', 'Liều dùng và cách dùng', 2000], ['contraindications', 'Chống chỉ định', 2000],
    ['sideEffects', 'Tác dụng không mong muốn', 2000], ['precautions', 'Thận trọng', 2000], ['overdoseManagement', 'Quá liều và xử trí', 1000],
] as const;
const reference = 'Thông tin thuốc chỉ mang tính tham khảo. Dùng thuốc theo chỉ định của bác sĩ/dược sĩ.';
function mount(path = '/medicines/501', state?: unknown) {
    return render(<MemoryRouter initialEntries={[{ pathname: path.split('?')[0], search: path.includes('?') ? '?' + path.split('?')[1] : '', state }]}>
        <Routes><Route path="/medicines/:id" element={<MedicineDetail />} /><Route path="/medicines" element={<p>Danh sách đích</p>} /></Routes>
    </MemoryRouter>);
}
beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: medicine });
    vi.mocked(axiosClient.post).mockResolvedValue({ success: true, data: { ...medicine, id: 502 } });
    vi.mocked(axiosClient.put).mockResolvedValue({ success: true, data: medicine });
});
afterEach(cleanup);

describe('Medicine detail', () => {
    it('renders metadata and populated article sections in reading order', async () => {
        mount();
        await screen.findByRole('heading', { name: medicine.name });
        expect(axiosClient.get).toHaveBeenCalledWith('/public/medicines/501');
        for (const text of ['DETAIL501', 'Ciprofloxacin', '500 mg', 'Viên nén', 'Còn hàng', 'Thuốc kê đơn']) expect(screen.getByText(text)).toBeInTheDocument();
        expect(screen.getAllByText('Kháng sinh')).toHaveLength(2);
        expect(screen.getByText(/6\.000\s*đ/)).toBeInTheDocument();
        const toc = screen.getByRole('navigation', { name: 'Mục lục thông tin thuốc' });
        expect(within(toc).getAllByRole('link').map(link => link.textContent)).toEqual(labels);
        expect(screen.getByText('Chỉ định thử nghiệm')).toBeVisible();
        expect(screen.queryByRole('heading', { name: 'Chống chỉ định' })).not.toBeInTheDocument();
        expect(screen.getByText(reference)).toBeInTheDocument();
        expect(screen.getByText('Thuốc này chỉ được bán theo đơn của bác sĩ. Vui lòng khám để được kê đơn.')).toBeInTheDocument();
    });

    it('renders error, warning and default interaction tags with descriptions and recommendations', async () => {
        const severities = ['Contraindicated', 'Major', 'Moderate', 'Minor'] as const;
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { ...medicine, interactions: severities.map(severity => ({ otherIngredient: 'other-' + severity, severity, description: 'Mô tả ' + severity, recommendation: 'Khuyến cáo ' + severity })) } });
        mount();
        await screen.findByRole('heading', { name: 'Tương tác thuốc' });
        for (const severity of severities) {
            expect(await screen.findByText(severity)).toHaveClass('ant-tag-' + (severity === 'Moderate' ? 'warning' : severity === 'Minor' ? 'default' : 'error'));
            expect(screen.getByText('other-' + severity)).toBeInTheDocument();
            expect(screen.getByText('Mô tả ' + severity)).toBeInTheDocument();
            expect(screen.getByText('Khuyến cáo ' + severity)).toBeInTheDocument();
        }
    });

    it('shows the reference for OTC drugs and omits empty interactions', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { ...medicine, isPrescriptionRequired: false } });
        mount();
        await screen.findByRole('heading', { name: medicine.name });
        expect(screen.getByText(reference)).toBeInTheDocument();
        expect(screen.queryByText('Thuốc này chỉ được bán theo đơn của bác sĩ. Vui lòng khám để được kê đơn.')).not.toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Tương tác thuốc' })).not.toBeInTheDocument();
    });

    it('separates loading from retryable errors and retries the same detail', async () => {
        let rejectRequest!: (error: unknown) => void;
        vi.mocked(axiosClient.get).mockReturnValueOnce(new Promise((_, reject) => { rejectRequest = reject; }));
        mount();
        expect(screen.getByText('Đang tải thông tin thuốc...')).toBeInTheDocument();
        expect(screen.queryByText('Không tìm thấy thuốc')).not.toBeInTheDocument();
        expect(screen.queryByText(reference)).not.toBeInTheDocument();
        rejectRequest({ status: 500, message: 'Lỗi tải thuốc' });
        expect(await screen.findByText('Lỗi tải thuốc')).toBeInTheDocument();
        expect(screen.queryByText('Đang tải thông tin thuốc...')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByRole('heading', { name: medicine.name });
        expect(vi.mocked(axiosClient.get).mock.calls.filter(call => call[0] === '/public/medicines/501')).toHaveLength(2);
    });

    it('renders a distinct 404 without the retry control', async () => {
        vi.mocked(axiosClient.get).mockRejectedValue({ status: 404, message: 'Not found' });
        mount();
        expect(await screen.findByText('Không tìm thấy thuốc')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Thử lại' })).not.toBeInTheDocument();
        expect(screen.queryByText(reference)).not.toBeInTheDocument();
    });

    it('rejects an invalid medicine id without requesting the API', async () => {
        mount('/medicines/invalid');
        expect(await screen.findByText('Không tìm thấy thuốc')).toBeInTheDocument();
        expect(axiosClient.get).not.toHaveBeenCalled();
    });

    it('ignores encoded prescriptionItem query data', async () => {
        mount('/medicines/501?prescriptionItem=' + encodeURIComponent(JSON.stringify({ dosage: 'Query dose', quantity: 99 })));
        await screen.findByRole('heading', { name: medicine.name });
        expect(screen.queryByText('Liều bác sĩ kê cho bạn')).not.toBeInTheDocument();
        expect(screen.queryByText('Query dose')).not.toBeInTheDocument();
    });

    it('relies on the breadcrumb instead of a separate back button', async () => {
        mount();
        await screen.findByRole('heading', { name: medicine.name });
        expect(screen.queryByRole('button', { name: 'Quay lại' })).not.toBeInTheDocument();
        expect(within(screen.getByRole('navigation', { name: 'Đường dẫn trang' })).getByRole('link', { name: 'Bảng giá thuốc' })).toHaveAttribute('href', '/medicines');
    });

    it('uses the shared image URL and replaces failed images', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { ...medicine, imageUrl: '/media/medicines/detail.png', availability: 'out_of_stock' } });
        mount();
        const image = await screen.findByRole('img', { name: medicine.name });
        expect(image).toHaveAttribute('src', 'http://localhost:5258/media/medicines/detail.png');
        fireEvent.error(image);
        expect(screen.getByRole('img', { name: 'Chưa có ảnh thuốc' })).toBeInTheDocument();
        expect(screen.getByText('Hết hàng')).toBeInTheDocument();
    });
});

describe('Medicine detail entry points', () => {
    it('wraps a linked card with the accessible name and keeps an unlinked card unchanged', () => {
        const view = render(<MemoryRouter><MedicineCard medicine={medicine} publicView to="/medicines/501" /></MemoryRouter>);
        const link = screen.getByRole('link', { name: 'Xem chi tiết Thuốc chi tiết' });
        expect(link).toHaveAttribute('href', '/medicines/501');
        expect(within(link).getByRole('heading', { name: medicine.name })).toBeInTheDocument();
        view.unmount();
        const plain = render(<MedicineCard medicine={medicine} publicView />);
        expect(screen.queryByRole('link')).not.toBeInTheDocument();
        expect(plain.container.firstElementChild).toHaveClass('ant-card');
    });

    it('opens medicine details from public prices', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: String(url) === '/public/medicines' ? { items: [medicine], totalItems: 1, totalPages: 1, page: 1, pageSize: 12 } : String(url) === '/public/medicine-categories' ? [] : medicine }));
        render(<MemoryRouter initialEntries={['/medicines']}><Routes><Route path="/medicines" element={<MedicinePrices />} /><Route path="/medicines/:id" element={<MedicineDetail />} /></Routes></MemoryRouter>);
        fireEvent.click(await screen.findByRole('link', { name: 'Xem chi tiết Thuốc chi tiết' }));
        expect(await screen.findByRole('heading', { name: medicine.name, level: 1 })).toBeInTheDocument();
    });

    it('opens patient prescription details with the prescribed dose in router state', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: String(url) === '/patients/me/prescriptions' ? [{
            id: 401, code: 'RX401', doctorName: 'Bác sĩ mẫu', specialtyName: 'Nội', diagnosis: 'Chẩn đoán', status: 'Issued', appointmentDate: '2026-10-16',
            items: [{ medicineId: 501, name: medicine.name, unit: 'Viên', quantity: 12, dosage: 'Liều riêng trong đơn', frequency: 'Tần suất riêng', durationDays: 6, instructions: 'Lời dặn riêng' }],
        }] : medicine }));
        render(<MemoryRouter initialEntries={['/patient/prescriptions']}><Routes>
            <Route path="/patient/prescriptions" element={<PatientPrescriptions />} /><Route path="/medicines/:id" element={<MedicineDetail />} />
        </Routes></MemoryRouter>);
        fireEvent.click(await screen.findByRole('link', { name: medicine.name }));
        expect(await screen.findByText('Liều bác sĩ kê cho bạn')).toBeInTheDocument();
        for (const text of ['Làm theo liều trong đơn, ưu tiên hơn hướng dẫn chung.', 'Liều riêng trong đơn', 'Tần suất riêng', '6 ngày', '12 Viên', 'Lời dặn riêng']) expect(screen.getByText(text)).toBeInTheDocument();
    });

    it('opens medicine details from pharmacy inventory', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: String(url) === '/medicines/active'
            ? [{ ...medicine, stockQuantity: 20, reorderLevel: 10 }]
            : String(url).includes('transactions') ? { items: [], totalItems: 0 } : medicine }));
        render(<MemoryRouter initialEntries={['/pharmacy/inventory']}><Routes>
            <Route path="/pharmacy/inventory" element={<PharmacyInventory />} /><Route path="/medicines/:id" element={<MedicineDetail />} />
        </Routes></MemoryRouter>);
        fireEvent.click(screen.getByText('Lưới'));
        fireEvent.click(await screen.findByRole('link', { name: 'Xem chi tiết Thuốc chi tiết' }));
        expect(await screen.findByRole('heading', { name: medicine.name, level: 1 })).toBeInTheDocument();
    });

    it.each(['create', 'update'])('sends all six monograph fields on admin %s with matching max lengths', async mode => {
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: String(url).includes('medicine-categories') ? [] : { items: [{ ...medicine, stockQuantity: 20, reorderLevel: 10, isActive: true }], totalItems: 1 } }));
        render(<AdminMedicines />);
        await screen.findByText(medicine.name);
        fireEvent.click(screen.getByRole('button', { name: mode === 'create' ? 'Thêm thuốc mới' : 'Sửa' }));
        if (mode === 'create') {
            fireEvent.change(screen.getByLabelText('Mã thuốc (*)'), { target: { value: 'DETAIL502' } });
            fireEvent.change(screen.getByLabelText('Tên thuốc (*)'), { target: { value: 'New detail' } });
        }
        const expected: Record<string, string> = {};
        for (const [field, label, max] of monographFields) {
            const input = screen.getByLabelText(label);
            expect(input.tagName).toBe('TEXTAREA');
            expect(input).toHaveAttribute('maxlength', String(max));
            expected[field] = 'Nội dung mới ' + field;
            fireEvent.change(input, { target: { value: expected[field] } });
        }
        fireEvent.click(screen.getByRole('button', { name: mode === 'create' ? /^Thêm thuốc$/ : 'Lưu thay đổi' }));
        await waitFor(() => expect(mode === 'create' ? axiosClient.post : axiosClient.put).toHaveBeenCalledWith(mode === 'create' ? '/admin/medicines' : '/admin/medicines/501', expect.objectContaining(expected)));
    });
});
