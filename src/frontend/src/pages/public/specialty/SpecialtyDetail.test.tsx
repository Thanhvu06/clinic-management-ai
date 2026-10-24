import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import axiosClient from '../../../api/axiosClient';
import { SpecialtyDetail } from '../SpecialtyDetail';

vi.mock('../../../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../../../components/chat/ChatConsultationLink', () => ({ ChatConsultationLink: ({ children, ...props }: { children: React.ReactNode }) => <a {...props}>{children}</a> }));
const specialty = { id: 2, specialtyCode: 'SP02', specialtyName: 'Nhi khoa', description: 'Mô tả khoa từ API', aiEnabled: true, consultationFee: 180000 };
const location = { id: 1, name: 'Cơ sở demo', address: 'Địa chỉ demo', openingHours: '07:00–17:00', phone: '028 123 456', isActive: true };
function mount() {
    return render(<MemoryRouter initialEntries={['/specialties/2']}><Routes><Route path="/specialties/:id" element={<SpecialtyDetail />} /></Routes></MemoryRouter>);
}
beforeEach(() => {
    vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: url === '/specialties/2' ? specialty : url === '/locations' ? [location] : [] }));
});
afterEach(cleanup);

describe('Rich specialty page', () => {
    it('shows the real fee and pediatric content, never the internal specialty code', async () => {
        mount();
        await screen.findByRole('heading', { name: 'Nhi khoa', level: 1 });
        expect(screen.getAllByText('180.000 đ / lượt')).toHaveLength(2);
        expect(screen.queryByText(/Mã khoa|SP02/)).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Khoa khám những bệnh gì' })).toBeInTheDocument();
        expect(screen.getByText('Chậm tăng cân')).toBeInTheDocument();
        expect(await screen.findByText('Địa chỉ demo')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: '028 123 456' })).toHaveAttribute('href', 'tel:028123456');
        expect(screen.getByText('30 phút')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Gói khám liên quan' })).not.toBeInTheDocument();
    });
    it('uses only the API description when a specialty has no editorial content', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: url === '/specialties/2'
            ? { ...specialty, specialtyCode: 'NEW', specialtyName: 'Khoa mới' } : [] }));
        mount();
        await screen.findByRole('heading', { name: 'Khoa mới', level: 1 });
        expect(screen.getByText('Mô tả khoa từ API')).toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Khoa khám những bệnh gì' })).not.toBeInTheDocument();
        expect(screen.getAllByRole('link', { name: 'Đặt lịch khám' })[0]).toHaveAttribute('href', '/patient/book?specialtyId=2');
    });
    it('retries locations separately without discarding clinical content', async () => {
        let failed = true;
        vi.mocked(axiosClient.get).mockImplementation(async url => {
            if (url === '/locations' && failed) throw { status: 503, message: 'Lỗi thông tin khám' };
            return { success: true, data: url === '/specialties/2' ? specialty : url === '/locations' ? [location] : [] };
        });
        mount();
        const error = await screen.findByRole('alert');
        expect(error).toHaveTextContent('Lỗi thông tin khám');
        expect(screen.getByText('Chậm tăng cân')).toBeInTheDocument();
        failed = false;
        fireEvent.click(within(error).getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByText('Địa chỉ demo')).toBeInTheDocument();
    });
});
