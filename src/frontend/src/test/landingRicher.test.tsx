import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PublicLanding } from '../pages/public/PublicLanding';
import { medicineImageUrl } from '../api/medicineApi';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn(), openChat: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
vi.mock('../contexts/ChatContext', () => ({ useChatContext: () => ({ openChat: mocks.openChat }) }));

const medicines = [
    { id: 1, name: 'Amoxicillin', strength: '500 mg', dosageForm: 'Viên nang', unit: 'viên', unitPrice: 2500, imageUrl: '/uploads/amoxicillin.webp', isPrescriptionRequired: true, availability: 'out_of_stock' },
    { id: 2, name: 'Paracetamol', strength: '500 mg', unit: 'viên', unitPrice: 1000, isPrescriptionRequired: false, availability: 'in_stock' },
];
const data: Record<string, unknown> = {
    '/doctors': [{ id: 7, fullName: 'Nguyễn Văn An', specialtyName: 'Tim mạch' }],
    '/specialties': [{ id: 1, specialtyName: 'Tim mạch', description: 'Khám tim và mạch máu' }],
    '/health-packages': [{ id: 3, name: 'Gói tổng quát', price: 1500000, includedServices: ['Khám nội'], targetAudience: 'Người lớn' }],
    '/locations': [{ id: 4, name: 'Phòng khám', address: '12 Lê Lợi', phone: '028 1234 5678', openingHours: '07:00–17:00' }],
    '/public/medicines': { items: medicines },
};
const mount = () => render(<MemoryRouter><PublicLanding /></MemoryRouter>);
beforeEach(() => {
    mocks.get.mockReset();
    mocks.openChat.mockReset();
    mocks.get.mockImplementation(async (url: string) => ({ success: true, data: data[url] ?? [] }));
});
afterEach(cleanup);

describe('Richer public landing', () => {
    it('loads ten medicines and displays the real image, price, prescription and stock labels without purchase actions', async () => {
        mount();
        const section = await screen.findByRole('region', { name: 'Thuốc tại nhà thuốc phòng khám' });
        expect(mocks.get).toHaveBeenCalledWith('/public/medicines', { params: { pageSize: 10 } });
        const card = within(section).getByRole('link', { name: /Amoxicillin/ });
        expect(card).toHaveAttribute('href', '/medicines/1');
        expect(within(card).getByRole('img')).toHaveAttribute('src', medicineImageUrl('/uploads/amoxicillin.webp'));
        expect(within(card).getByRole('img')).toHaveAttribute('loading', 'lazy');
        expect(card).toHaveTextContent('500 mg · Viên nang');
        expect(card).toHaveTextContent('2.500 đ / viên');
        expect(within(card).getByText('Cần đơn bác sĩ')).toBeInTheDocument();
        expect(within(card).getByText('Hết hàng')).toBeInTheDocument();
        expect(within(section).getAllByText('Cần đơn bác sĩ')).toHaveLength(1);
        expect(within(section).queryByRole('button')).not.toBeInTheDocument();
        expect(within(section).getByRole('link', { name: 'Xem bảng giá thuốc' })).toHaveAttribute('href', '/medicines');
    });

    it.each(['/specialties', '/doctors', '/health-packages', '/locations', '/public/medicines'])('isolates a failed %s API and keeps the rest of the page', async failed => {
        mocks.get.mockImplementation(async (url: string) => {
            if (url === failed) throw new Error('Unavailable');
            return { success: true, data: data[url] ?? [] };
        });
        mount();
        await screen.findByText('Hiện chưa có lịch trống trong 7 ngày tới');
        const sections = [
            ['/specialties', 'Chuyên khoa'], ['/doctors', 'Bác sĩ'], ['/health-packages', 'Gói khám'],
            ['/locations', 'Liên hệ'], ['/public/medicines', 'Thuốc tại nhà thuốc phòng khám'],
        ];
        for (const [endpoint, title] of sections) {
            if (endpoint === failed) expect(screen.queryByRole('heading', { level: 2, name: title })).not.toBeInTheDocument();
            else expect(screen.getByRole('heading', { level: 2, name: title })).toBeInTheDocument();
        }
        expect(screen.getByRole('heading', { name: 'Câu hỏi thường gặp' })).toBeInTheDocument();
    });

    it('keeps the E2E strings, lookup modal, AI action, contact directions and supplied hero', async () => {
        mount();
        await screen.findByRole('heading', { name: 'Liên hệ' });
        expect(screen.getByRole('heading', { name: 'Đặt lịch khám tại ClinicCare AI' })).toBeInTheDocument();
        expect(screen.getByPlaceholderText('Tìm bác sĩ, chuyên khoa, gói khám')).toHaveAttribute('aria-label', 'Tìm bác sĩ, chuyên khoa, gói khám');
        expect(screen.getByRole('button', { name: 'Tìm kiếm' })).toBeInTheDocument();
        const shortcuts = screen.getByRole('navigation', { name: 'Lối tắt' });
        for (const text of ['Đặt lịch khám', 'Bảng giá thuốc', 'Hỏi trợ lý AI', 'Gói khám']) expect(within(shortcuts).getByRole('link', { name: text })).toBeInTheDocument();
        fireEvent.click(within(shortcuts).getByRole('link', { name: 'Hỏi trợ lý AI' }));
        expect(mocks.openChat).toHaveBeenCalledOnce();
        fireEvent.click(within(shortcuts).getByRole('button', { name: 'Tra cứu lịch hẹn' }));
        expect(screen.getByPlaceholderText(/APT-/)).toBeInTheDocument();
        expect(screen.getByAltText('Hành lang sáng thoáng tại cơ sở y tế')).toHaveAttribute('width', '1600');
        expect(screen.getByText('Ảnh minh họa: Unsplash')).toBeInTheDocument();
        const directions = screen.getByRole('link', { name: 'Chỉ đường' });
        expect(directions).toHaveAttribute('href', 'https://www.google.com/maps/search/?api=1&query=12%20L%C3%AA%20L%E1%BB%A3i');
        expect(directions).toHaveAttribute('rel', 'noopener noreferrer');
    });
});
