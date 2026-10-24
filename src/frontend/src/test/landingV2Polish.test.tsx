import { cleanup, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EarliestSlots } from '../pages/public/landing/EarliestSlots';
import { MedicineSection } from '../pages/public/landing/MedicineSection';
import { PackageSection } from '../pages/public/landing/PackageSection';
import { SpecialtySection } from '../pages/public/landing/SpecialtySection';
import type { LandingPackage } from '../pages/public/landing/landingTypes';
import type { PublicMedicineDto } from '../types';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn(), openChat: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
vi.mock('../contexts/ChatContext', () => ({ useChatContext: () => ({ openChat: mocks.openChat }) }));

const doctors = (count: number) => Array.from({ length: count }, (_, index) => ({ id: index + 1, fullName: `Bác sĩ ${index + 1}` }));
const mountSlots = (count: number) => render(<MemoryRouter><EarliestSlots doctors={doctors(count)} loading={false} /></MemoryRouter>);

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-24T01:30:00Z'));
    mocks.get.mockReset();
    mocks.get.mockImplementation(async (url: string) => {
        const id = Number(url.split('/')[2]);
        return { success: true, data: [{ slotId: id, slotDate: '2026-10-26', startTime: `${String(7 + id).padStart(2, '0')}:00:00`, endTime: '18:00:00' }] };
    });
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('Landing v2 polish', () => {
    it('shows at most four full slot rows and links to more open slots', async () => {
        mountSlots(6);
        await screen.findByText('Bác sĩ 1');
        const panel = screen.getByRole('complementary', { name: 'Lịch khám còn trống sớm nhất' });
        expect(within(panel).getAllByRole('listitem')).toHaveLength(4);
        expect(within(panel).queryByText('Bác sĩ 5')).not.toBeInTheDocument();
        expect(within(panel).getByRole('link', { name: 'Xem thêm lịch trống' })).toHaveAttribute('href', '/patient/book');
    });

    it('omits the more-slots link when four or fewer slots exist', async () => {
        mountSlots(3);
        await screen.findByText('Bác sĩ 3');
        expect(screen.getAllByRole('listitem')).toHaveLength(3);
        expect(screen.queryByRole('link', { name: 'Xem thêm lịch trống' })).not.toBeInTheDocument();
    });

    it('shows one featured package plus four others and leaves the rest to the all-packages link', () => {
        const packages = Array.from({ length: 7 }, (_, index) => ({ id: index + 1, name: index === 2 ? 'Gói khám tổng quát' : `Gói ${index + 1}`, price: 100000, includedServices: ['Khám'] })) as LandingPackage[];
        render(<MemoryRouter><PackageSection packages={packages} /></MemoryRouter>);
        const section = screen.getByRole('region', { name: 'Gói khám' });
        const cards = within(section).getAllByRole('article');
        expect(cards).toHaveLength(5);
        expect(cards[0]).toHaveTextContent('Gói khám tổng quát');
        expect(within(section).getByRole('link', { name: 'Tất cả gói khám' })).toHaveAttribute('href', '/health-packages');
    });

    it('shows short specialty descriptions in full and only the name for long ones', () => {
        const long = 'Khám và điều trị các bệnh lý tim mạch, tăng huyết áp, rối loạn nhịp tim ở người lớn';
        render(<MemoryRouter><SpecialtySection specialties={[
            { id: 1, specialtyName: 'Nhi khoa', description: 'Khám cho trẻ em' },
            { id: 2, specialtyName: 'Tim mạch', description: long },
        ]} /></MemoryRouter>);
        expect(screen.getByRole('link', { name: /Nhi khoa/ })).toHaveTextContent('Khám cho trẻ em');
        expect(screen.getByRole('link', { name: /Tim mạch/ })).toHaveTextContent(/^Tim mạch$/);
    });

    it('keeps the price as the last body row and places labels on the image', () => {
        const medicines = [
            { id: 1, name: 'Amoxicillin', unit: 'viên', unitPrice: 2500, imageUrl: '/uploads/a.webp', isPrescriptionRequired: true, availability: 'in_stock' },
            { id: 2, name: 'Paracetamol', unit: 'viên', unitPrice: 1000, isPrescriptionRequired: false, availability: 'in_stock' },
        ] as PublicMedicineDto[];
        render(<MemoryRouter><MedicineSection medicines={medicines} /></MemoryRouter>);
        for (const name of ['Amoxicillin', 'Paracetamol']) {
            const card = screen.getByRole('link', { name: new RegExp(name) });
            const body = within(card).getByRole('heading', { level: 3 }).parentElement!;
            expect(body.lastElementChild).toHaveTextContent(/đ \/ viên$/);
        }
        const label = screen.getByText('Cần đơn bác sĩ');
        expect(label.closest('a')?.querySelector('img')?.parentElement).toContainElement(label);
    });
});
