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
    it('lists only doctors not already recommended under the team section', async () => {
        const doctor = (id: number, fullName: string) => ({ id, fullName, experienceYears: 5 });
        const recommended = (doctorId: number, fullName: string) => ({ doctorId, fullName, experienceYears: 5, experienceScore: .5, availabilityScore: .5, totalScore: .5, earliestAvailableSlot: null });
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: url === '/specialties/2' ? specialty
            : url === '/specialties/2/doctors?page=1&pageSize=20' ? [doctor(1, 'Bác sĩ Một'), doctor(2, 'Bác sĩ Hai'), doctor(3, 'Bác sĩ Ba')]
                : url === '/specialties/2/recommended-doctors' ? [recommended(1, 'Bác sĩ Một')] : [] }));
        mount();
        const team = await screen.findByRole('region', { name: 'Bác sĩ của khoa (2)' });
        const recommendations = screen.getByRole('region', { name: 'Bác sĩ được gợi ý' });
        expect(within(recommendations).getByRole('heading', { name: 'Bác sĩ Một' })).toBeInTheDocument();
        expect(within(team).queryByRole('heading', { name: 'Bác sĩ Một' })).not.toBeInTheDocument();
        expect(within(team).getAllByRole('heading', { level: 3 }).map(item => item.textContent)).toEqual(['Bác sĩ Hai', 'Bác sĩ Ba']);
    });
    it('hides the team section when every doctor is already recommended', async () => {
        vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, data: url === '/specialties/2' ? specialty
            : url === '/specialties/2/doctors?page=1&pageSize=20' ? [{ id: 1, fullName: 'Bác sĩ Một', experienceYears: 5 }]
                : url === '/specialties/2/recommended-doctors' ? [{ doctorId: 1, fullName: 'Bác sĩ Một', experienceYears: 5, experienceScore: .5, availabilityScore: .5, totalScore: .5, earliestAvailableSlot: null }] : [] }));
        mount();
        await screen.findByRole('region', { name: 'Bác sĩ được gợi ý' });
        expect(screen.getAllByRole('heading', { name: 'Bác sĩ Một' })).toHaveLength(1);
        expect(screen.queryByRole('heading', { name: /Bác sĩ của khoa/ })).not.toBeInTheDocument();
        expect(screen.queryByText(/Khoa chưa có bác sĩ nhận lịch trực tuyến/)).not.toBeInTheDocument();
    });
});
