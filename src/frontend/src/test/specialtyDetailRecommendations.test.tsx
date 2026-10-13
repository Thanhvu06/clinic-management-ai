import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SpecialtyDetail } from '../pages/public/SpecialtyDetail';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));

const recommendationsUrl = '/specialties/2/recommended-doctors';
const specialty = { id: 2, specialtyCode: 'NOI', specialtyName: 'Nội khoa', description: 'Chăm sóc sức khỏe nội khoa.', aiEnabled: true };
const doctors = [{ id: 7, fullName: 'Nguyễn Văn An', academicTitle: 'BS', experienceYears: 12 }];
const recommendations = [
    { doctorId: 7, fullName: 'Nguyễn Văn An', experienceYears: 12, experienceScore: 0.7, availabilityScore: 0.9, totalScore: 0.82, earliestAvailableSlot: '2026-10-14T00:30:00+07:00' },
    { doctorId: 8, fullName: 'Trần Thị Bình', experienceYears: 10, experienceScore: 0.6, availabilityScore: 0.8, totalScore: 0.72, earliestAvailableSlot: null },
    { doctorId: 9, fullName: 'Lê Hoàng Nam', experienceYears: 8, experienceScore: 0.5, availabilityScore: 0.7, totalScore: 0.62, earliestAvailableSlot: '2026-10-15T08:00:00+07:00' },
    { doctorId: 10, fullName: 'Bác sĩ thứ tư', experienceYears: 5, experienceScore: 0.4, availabilityScore: 0.6, totalScore: 0.52, earliestAvailableSlot: null },
];
const response = (data: unknown) => ({ success: true, data });
const RouteProbe = () => {
    const location = useLocation();
    return <output aria-label="Đường dẫn hiện tại">{location.pathname}{location.search}</output>;
};
function mockApi(recommendedResponse: () => Promise<unknown> = async () => response(recommendations)) {
    mocks.get.mockImplementation(async (url: string) => {
        if (url === recommendationsUrl) return recommendedResponse();
        if (url === '/specialties/2') return response(specialty);
        if (url === '/specialties/2/doctors?page=1&pageSize=20') return response({ items: doctors });
        throw new Error(`Unexpected API: ${url}`);
    });
}
function mount() {
    return render(<ConfigProvider theme={{ token: { motion: false } }}>
        <MemoryRouter initialEntries={['/specialties/2']}>
            <Routes><Route path="/specialties/:id" element={<SpecialtyDetail />} /><Route path="*" element={<div>Trang đích</div>} /></Routes>
            <RouteProbe />
        </MemoryRouter>
    </ConfigProvider>);
}
async function recommendationsRegion() {
    return screen.findByRole('region', { name: 'Bác sĩ được gợi ý' });
}
beforeEach(() => { mocks.get.mockReset(); mockApi(); });
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('Specialty doctor recommendations', () => {
    it('shows only the first three ranked doctors, the best match, percentage and Vietnam slot time', async () => {
        mount();
        const region = await recommendationsRegion();
        const cards = within(region).getAllByRole('article');
        expect(cards).toHaveLength(3);
        expect(within(cards[0]).getByText('Phù hợp nhất')).toBeInTheDocument();
        expect(within(region).getAllByText('Phù hợp nhất')).toHaveLength(1);
        expect(within(cards[0]).getByRole('heading', { name: 'BS. Nguyễn Văn An' })).toBeInTheDocument();
        expect(within(cards[1]).getByRole('heading', { name: 'Trần Thị Bình' })).toBeInTheDocument();
        expect(within(cards[0]).getByText('12 năm kinh nghiệm')).toBeInTheDocument();
        expect(within(cards[0]).getByText('82%')).toBeInTheDocument();
        expect(within(cards[0]).getByRole('progressbar')).toHaveAttribute('aria-valuenow', '82');
        expect(within(cards[0]).getByText('Lịch trống sớm nhất: Thứ Tư, 14/10, 00:30')).toBeInTheDocument();
        expect(within(region).queryByText('Bác sĩ thứ tư')).not.toBeInTheDocument();
        expect(within(region).getByText('Gợi ý dựa trên kinh nghiệm và lịch trống gần nhất của bác sĩ.')).toBeInTheDocument();
        const teamHeading = screen.getByRole('heading', { name: 'Đội ngũ Bác sĩ phụ trách (1)' });
        expect(region.compareDocumentPosition(teamHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(screen.queryByText('Chỉ định thăm khám thường gặp')).not.toBeInTheDocument();
    }, 15000);

    it('shows the no-slot message and omits date for a doctor without availability', async () => {
        mount();
        const region = await recommendationsRegion();
        const card = within(region).getAllByRole('article')[1];
        expect(within(card).getByText('Chưa có lịch trống trong 14 ngày tới')).toBeInTheDocument();
        expect(within(card).getByRole('link', { name: 'Đặt lịch' })).toHaveAttribute('href', '/patient/book?specialtyId=2&doctorId=8');
    }, 15000);

    it('links to booking with the Vietnam calendar date and to the doctor profile, using SPA navigation', async () => {
        mount();
        const region = await recommendationsRegion();
        const card = within(region).getAllByRole('article')[0];
        const booking = within(card).getByRole('link', { name: 'Đặt lịch' });
        expect(booking).toHaveAttribute('href', '/patient/book?specialtyId=2&doctorId=7&date=2026-10-14');
        expect(within(card).getByRole('link', { name: 'Hồ sơ' })).toHaveAttribute('href', '/doctors/7');
        const probe = screen.getByLabelText('Đường dẫn hiện tại');
        fireEvent.click(booking);
        expect(probe).toHaveTextContent('/patient/book?specialtyId=2&doctorId=7&date=2026-10-14');
        expect(screen.getByLabelText('Đường dẫn hiện tại')).toBe(probe);
    }, 15000);

    it('isolates recommendation failure and retries only its API', async () => {
        const recommendedResponse = vi.fn().mockRejectedValueOnce({ message: 'Không thể tải bác sĩ được gợi ý.' }).mockResolvedValueOnce(response(recommendations));
        mockApi(recommendedResponse);
        mount();
        const region = await recommendationsRegion();
        expect(within(region).getByRole('alert')).toHaveTextContent('Không thể tải bác sĩ được gợi ý.');
        expect(screen.getByRole('heading', { name: 'Nội khoa', level: 1 })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Đội ngũ Bác sĩ phụ trách (1)' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'BS. Nguyễn Văn An' })).toBeInTheDocument();
        fireEvent.click(within(region).getByRole('button', { name: 'Thử lại' }));
        await within(region).findByText('82%');
        expect(mocks.get.mock.calls.map(call => call[0])).toEqual([
            '/specialties/2', '/specialties/2/doctors?page=1&pageSize=20', recommendationsUrl, recommendationsUrl,
        ]);
    }, 15000);

    it('requests today in local calendar format and a 14-day window', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date(2026, 9, 13, 0, 15));
        mount();
        await recommendationsRegion();
        expect(mocks.get).toHaveBeenCalledWith(recommendationsUrl, expect.objectContaining({ params: { fromDate: '2026-10-13', days: 14 } }));
    }, 15000);

    it('hides the whole recommendations section for an empty array', async () => {
        mockApi(async () => response([]));
        mount();
        await screen.findByRole('heading', { name: 'Nội khoa', level: 1 });
        expect(screen.queryByRole('region', { name: 'Bác sĩ được gợi ý' })).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Đội ngũ Bác sĩ phụ trách (1)' })).toBeInTheDocument();
    }, 15000);

    it('loads recommendations independently while displaying the specialty and doctors', async () => {
        let resolveRecommendations!: (value: unknown) => void;
        mockApi(() => new Promise(resolve => { resolveRecommendations = resolve; }));
        mount();
        const region = await recommendationsRegion();
        expect(within(region).getByRole('status', { name: 'Đang tải bác sĩ được gợi ý' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Đội ngũ Bác sĩ phụ trách (1)' })).toBeInTheDocument();
        await act(async () => { resolveRecommendations(response(recommendations)); });
        expect(within(region).queryByRole('status')).not.toBeInTheDocument();
        expect(within(region).getAllByRole('article')).toHaveLength(3);
    }, 15000);

    it('treats an unsuccessful API response as a retryable recommendation error', async () => {
        mockApi(async () => ({ success: false, message: 'Gợi ý tạm thời không khả dụng.', data: [] }));
        mount();
        const region = await recommendationsRegion();
        expect(within(region).getByRole('alert')).toHaveTextContent('Gợi ý tạm thời không khả dụng.');
        expect(within(region).getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
    }, 15000);

    it('retries a page load failure and keeps existing booking and AI destinations', async () => {
        mocks.get.mockRejectedValueOnce({ message: 'Không thể tải thông tin chuyên khoa.' });
        mount();
        expect(await screen.findByRole('alert')).toHaveTextContent('Không thể tải thông tin chuyên khoa.');
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByRole('heading', { name: 'Nội khoa', level: 1 });
        expect(screen.getByRole('link', { name: 'Đặt lịch khám ngay' })).toHaveAttribute('href', '/patient/book?specialtyId=2');
        expect(screen.getByRole('link', { name: 'Đặt khám với bác sĩ' })).toHaveAttribute('href', '/patient/book?specialtyId=2&doctorId=7');
        expect(screen.getByRole('link', { name: 'Định tuyến triệu chứng AI' })).toHaveAttribute('href', '/patient/ai-consultation');
        expect(mocks.get.mock.calls.filter(call => call[0] === recommendationsUrl)).toHaveLength(1);
    }, 15000);

    it('preserves the not-found message and return link for a missing specialty', async () => {
        mocks.get.mockRejectedValueOnce({ status: 404 });
        mount();
        await waitFor(() => expect(screen.getByText('Không tìm thấy thông tin chuyên khoa.')).toBeInTheDocument());
        expect(screen.getByRole('link', { name: '← Quay lại danh sách chuyên khoa' })).toHaveAttribute('href', '/specialties');
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    }, 15000);
});
