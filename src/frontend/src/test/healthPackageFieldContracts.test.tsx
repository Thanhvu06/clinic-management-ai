import { cleanup, fireEvent, render as renderPage, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AdminHealthPackages } from '../pages/admin/AdminHealthPackages';
import { ReceptionPackageRegistrations } from '../pages/reception/ReceptionPackageRegistrations';
import { LocationsList } from '../pages/public/LocationsList';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => dialog }));

const render = (page: ReactNode) => renderPage(<MemoryRouter><ConfigProvider theme={{ token: { motion: false } }}>{page}</ConfigProvider></MemoryRouter>);
const backendPackage = {
    id: 5, code: 'PKG-ADULT', name: 'Gói người lớn', targetAudience: 'Người lớn', description: 'Khám tổng quát định kỳ',
    price: 1500000, imageUrl: null, includedServices: ['Khám nội', 'Xét nghiệm máu'], isActive: true, createdAt: '2026-10-01T00:00:00Z'
};

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.post).mockResolvedValue({ success: true, data: { id: 9 } });
    vi.mocked(axiosClient.put).mockResolvedValue({ success: true, data: backendPackage });
});
afterEach(cleanup);

describe('Admin health package form', () => {
    beforeEach(() => {
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { items: [backendPackage], totalItems: 1 } });
    });

    it('shows targetAudience in the table and sends it with a service list when creating', async () => {
        render(<AdminHealthPackages />);
        expect(await screen.findByText('Người lớn')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /Thêm gói khám mới/ }));
        fireEvent.change(screen.getByLabelText('Mã gói (*)'), { target: { value: 'PKG-NEW' } });
        fireEvent.change(screen.getByLabelText('Tên gói khám (*)'), { target: { value: 'Gói mới' } });
        fireEvent.change(screen.getByLabelText('Đối tượng phù hợp (*)'), { target: { value: ' Phụ nữ ' } });
        fireEvent.change(screen.getByLabelText('Mô tả ngắn (*)'), { target: { value: 'Mô tả gói' } });
        fireEvent.change(screen.getByLabelText(/Danh mục dịch vụ bao gồm/), { target: { value: 'Khám phụ khoa\nSiêu âm\n' } });
        fireEvent.click(screen.getByRole('button', { name: 'Tạo gói khám' }));

        await waitFor(() => expect(axiosClient.post).toHaveBeenCalledTimes(1));
        const payload = vi.mocked(axiosClient.post).mock.calls[0][1] as Record<string, unknown>;
        expect(vi.mocked(axiosClient.post).mock.calls[0][0]).toBe('/admin/health-packages');
        expect(payload.targetAudience).toBe('Phụ nữ');
        expect(payload).not.toHaveProperty('targetGroup');
        expect(payload.includedServices).toEqual(['Khám phụ khoa', 'Siêu âm']);
    });

    it('blocks submit without targetAudience and shows the form error', async () => {
        render(<AdminHealthPackages />);
        await screen.findByText('Người lớn');
        fireEvent.click(screen.getByRole('button', { name: /Thêm gói khám mới/ }));
        fireEvent.change(screen.getByLabelText('Mã gói (*)'), { target: { value: 'PKG-EMPTY' } });
        fireEvent.change(screen.getByLabelText('Tên gói khám (*)'), { target: { value: 'Gói thiếu đối tượng' } });
        fireEvent.change(screen.getByLabelText('Mô tả ngắn (*)'), { target: { value: 'Mô tả' } });
        fireEvent.submit(screen.getByRole('button', { name: 'Tạo gói khám' }).closest('form')!);

        expect(await screen.findByText('Vui lòng nhập đối tượng phù hợp và mô tả ngắn.')).toBeInTheDocument();
        expect(axiosClient.post).not.toHaveBeenCalled();
    });

    it('prefills targetAudience and services when editing and sends them back on update', async () => {
        render(<AdminHealthPackages />);
        await screen.findByText('Người lớn');
        fireEvent.click(screen.getByRole('button', { name: /Sửa/ }));

        expect(screen.getByLabelText('Đối tượng phù hợp (*)')).toHaveValue('Người lớn');
        expect(screen.getByLabelText(/Danh mục dịch vụ bao gồm/)).toHaveValue('Khám nội\nXét nghiệm máu');
        fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));

        await waitFor(() => expect(axiosClient.put).toHaveBeenCalledTimes(1));
        const [url, payload] = vi.mocked(axiosClient.put).mock.calls[0] as [string, Record<string, unknown>];
        expect(url).toBe('/admin/health-packages/5');
        expect(payload.targetAudience).toBe('Người lớn');
        expect(payload).not.toHaveProperty('targetGroup');
        expect(payload.includedServices).toEqual(['Khám nội', 'Xét nghiệm máu']);
    });
});

it('names the healthPackageName returned by the backend in the reception confirm dialog', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { items: [{
        id: 21, registrationCode: 'PKG-REG-21', healthPackageId: 5, healthPackageCode: 'PKG-ADULT', healthPackageName: 'Gói người lớn',
        healthPackagePrice: 1500000, patientId: 17, patientName: 'Người bệnh gói', patientPhone: '0901234567',
        preferredDate: '2026-10-28', contactPhone: '0901234567', status: 'Pending', createdAt: '2026-10-20T03:00:00Z'
    }], totalItems: 1 } });
    render(<ReceptionPackageRegistrations />);

    const row = (await screen.findByText('PKG-REG-21')).closest('tr')!;
    fireEvent.click(within(row).getByRole('button', { name: /Xác nhận/ }));
    const dialogBox = await screen.findByRole('dialog');
    expect(within(dialogBox).getByText('Gói người lớn', { selector: 'strong' })).toBeInTheDocument();
});

it('renders location features returned by the backend', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: [{
        id: 1, code: 'HCM-01', name: 'Cơ sở Bến Thành', address: '1 Lê Lợi', city: 'TP. Hồ Chí Minh', phone: '0280000000',
        openingHours: '07:00 - 17:00', description: null, features: ['Bãi đỗ xe', 'Xét nghiệm nhanh'], isActive: true
    }] });
    render(<LocationsList />);

    expect(await screen.findByText('Bãi đỗ xe')).toBeInTheDocument();
    expect(screen.getByText('Xét nghiệm nhanh')).toBeInTheDocument();
});
