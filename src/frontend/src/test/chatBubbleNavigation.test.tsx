import { StrictMode } from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { Link, MemoryRouter, Route, Routes, useLocation, useNavigate, useNavigationType } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { DialogProvider } from '../contexts/DialogContext';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatConsultationRedirect, ChatRouteHistory } from '../components/chat/ChatConsultationRedirect';
import { PublicLayout } from '../layouts/PublicLayout';
import { PublicLanding } from '../pages/public/PublicLanding';
import { SearchResults } from '../pages/public/SearchResults';
import { SpecialtyDetail } from '../pages/public/SpecialtyDetail';
import type { AiBookingWizardResponse, ReviewBookingAction } from '../types/ai';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ role: 'Patient' as string | null, get: vi.fn(), post: vi.fn() }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({
    user: mocks.role ? { userId: 'chat-navigation', fullName: 'Người kiểm thử', role: mocks.role } : null,
    isAuthenticated: Boolean(mocks.role), identityVersion: 1, logout: vi.fn(),
}) }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => ({
    role: 'Patient', suggestions: [{ code: 'patient.start_booking', label: 'Đặt lịch khám' }],
}) }));

const RouteProbe = () => {
    const location = useLocation();
    const navigation = useNavigationType();
    const navigate = useNavigate();
    return <><output aria-label="Địa chỉ">{location.pathname}{location.search}{location.hash}</output>
        <output aria-label="Kiểu điều hướng">{navigation}</output>
        <button onClick={() => void navigate(-1)}>Lùi trang</button></>;
};
const surface = (route: string) => <StrictMode><ConfigProvider theme={{ token: { motion: false } }}>
    <DialogProvider><MemoryRouter initialEntries={[route]}><ChatProvider><ChatRouteHistory>
        <MedicalChatWidget /><RouteProbe />
        <Routes><Route element={<PublicLayout />}>
            <Route path="/" element={<PublicLanding />} />
            <Route path="/search" element={<SearchResults />} />
            <Route path="/patient/book" element={<BookAppointment />} />
            <Route path="/specialties/:id" element={<SpecialtyDetail />} />
            <Route path="/before" element={<Link to="/patient/ai-consultation">Mở liên kết cũ</Link>} />
            <Route path="/patient/ai-consultation" element={<ChatConsultationRedirect />} />
        </Route></Routes>
    </ChatRouteHistory></ChatProvider></MemoryRouter></DialogProvider>
</ConfigProvider></StrictMode>;
const mount = async (route = '/') => {
    let view: ReturnType<typeof render>;
    await act(async () => { view = render(surface(route)); });
    return view!;
};
const click = async (element: HTMLElement) => { await act(async () => { fireEvent.click(element); }); };
const dialog = () => screen.getByRole('dialog', { name: /ClinicCare AI/ });

beforeEach(() => {
    mocks.role = 'Patient';
    mocks.get.mockReset(); mocks.post.mockReset(); sessionStorage.clear(); localStorage.clear();
    mocks.get.mockImplementation(async (url: string) => ({ success: true, data:
        url === '/specialties/1' ? { id: 1, specialtyName: 'Nội khoa', aiEnabled: true } : [],
    }));
});
afterEach(cleanup);

describe('Chat bubble entry points', () => {
    it.each([0, 1, 2])('opens from desktop/mobile/footer link %i and keeps the URL and href', async index => {
        await mount('/before?source=menu');
        const links = [...document.querySelectorAll<HTMLAnchorElement>('a[href="/patient/ai-consultation"]')]
            .filter(link => link.textContent !== 'Mở liên kết cũ');
        expect(links).toHaveLength(3);
        await click(links[index]);
        expect(dialog()).toBeInTheDocument();
        expect(screen.getByLabelText('Địa chỉ')).toHaveTextContent('/before?source=menu');
        await click(within(dialog()).getByRole('button', { name: 'Đóng' }));
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        await click(links[index]);
        expect(dialog()).toBeInTheDocument();
    });

    it.each(['Patient', null])('replaces a direct legacy URL with home and opens the bubble for %s', async role => {
        mocks.role = role;
        await mount('/patient/ai-consultation');
        await waitFor(() => expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/'));
        expect(screen.getByLabelText('Kiểu điều hướng')).toHaveTextContent('REPLACE');
        expect(dialog()).toBeInTheDocument();
    });

    it('keeps the bubble open while authentication restores the patient after a direct legacy URL', async () => {
        mocks.role = null;
        const view = await mount('/patient/ai-consultation');
        expect(dialog()).toBeInTheDocument();
        mocks.role = 'Patient';
        await act(async () => { view.rerender(surface('/patient/ai-consultation')); });
        expect(dialog()).toBeInTheDocument();
        expect(within(dialog()).getByRole('textbox', { name: 'Nội dung tin nhắn gửi tới ClinicCare AI' })).toBeInTheDocument();
        expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/');
    });

    it('replaces the legacy route with the previous route including query and hash', async () => {
        await mount('/before?source=old#details');
        await click(screen.getByRole('link', { name: 'Mở liên kết cũ' }));
        await waitFor(() => expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/before?source=old#details'));
        expect(screen.getByLabelText('Kiểu điều hướng')).toHaveTextContent('REPLACE');
        expect(dialog()).toBeInTheDocument();
        await click(screen.getByRole('button', { name: 'Lùi trang' }));
        expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/before?source=old#details');
    });

    it.each(['Lối tắt'])('opens from home %s without navigating', async navigation => {
        await mount();
        await click(within(screen.getByRole('navigation', { name: navigation })).getByRole('link', { name: 'Hỏi trợ lý AI' }));
        expect(dialog()).toBeInTheDocument();
        expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/');
    });

    it.each([0, 1])('opens from landing AI link %i without changing its href or the URL', async index => {
        await mount();
        const link = screen.getAllByRole('link', { name: 'Hỏi trợ lý AI' })[index];
        expect(link).toHaveAttribute('href', '/patient/ai-consultation');
        await click(link);
        expect(dialog()).toBeInTheDocument();
        expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/');
    });

    it.each([0, 1, 2])('keeps the staff modal for desktop/mobile/footer link %i', async index => {
        mocks.role = 'Doctor';
        await mount('/before');
        const links = [...document.querySelectorAll<HTMLAnchorElement>('a[href="/patient/ai-consultation"]')]
            .filter(link => link.textContent !== 'Mở liên kết cũ');
        await click(links[index]);
        expect(screen.getByRole('heading', { name: 'Tính năng dành cho người bệnh' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Về bàn làm việc' })).toBeInTheDocument();
        expect(screen.queryByRole('dialog', { name: /ClinicCare AI/ })).not.toBeInTheDocument();
        expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/before');
    });

    it.each([
        ['/patient/book', 'Hỏi trợ lý AI'],
        ['/search?q=unknown', 'Hỏi trợ lý AI nên khám chuyên khoa nào'],
        ['/specialties/1', 'Hỏi trợ lý AI'],
    ])('opens the bubble from %s without navigation', async (route, name) => {
        await mount(route);
        const link = await within(screen.getByRole('main')).findByRole('link', { name });
        expect(link).toHaveAttribute('href', '/patient/ai-consultation');
        await click(link);
        expect(dialog()).toBeInTheDocument();
        expect(screen.getByLabelText('Địa chỉ').textContent).toBe(route);
    });

    it('shows suggestion chips and completes the booking wizard in the bubble opened by the menu', async () => {
        const review: ReviewBookingAction = {
            id: 'review', type: 'ReviewBooking', label: 'Xem tóm tắt thông tin khám', style: 'secondary',
            requiresAuthentication: true, requiresConfirmation: false, draftVersion: 1,
            payload: { specialtyId: 1, specialtyName: 'Nội khoa', doctorId: 2, doctorName: 'Bác sĩ An', slotId: 3,
                slotDate: '2030-01-02', startTime: '08:00', endTime: '08:30', reason: 'Khám tổng quát', draftVersion: 1,
                draftId: 'bubble-draft', sessionId: 'bubble-session', contextSnapshotId: 'bubble-snapshot', confirmationId: 'bubble-confirmation' },
        };
        const steps: Array<[AiBookingWizardResponse['step'], string?]> = [
            ['specialty', 'Nội khoa'], ['doctor', 'Bác sĩ An'], ['day', '02/01/2030'],
            ['slot', '08:00 – 08:30'], ['reason'], ['review'],
        ];
        steps.forEach(([step, label]) => mocks.post.mockResolvedValueOnce({ success: true, data: {
            step, title: 'Đặt lịch khám', message: 'Chọn để tiếp tục.', options: label ? [{ token: `token-${step}`, label }] : [],
            canGoBack: step !== 'specialty', reasonToken: 'reason-token', actions: [], suggestions: [],
            assistantMode: 'Ready', providerWasCalled: false, ...(step === 'review' ? { reviewAction: review } : {}),
        } }));
        mocks.post.mockResolvedValueOnce({ success: true, data: { id: 42, appointmentCode: 'BUBBLE-42', ...review.payload } });
        await mount('/before');
        await click(screen.getAllByRole('link', { name: 'Trợ lý AI' })[0]);
        expect(dialog().querySelector('[data-provider-status]')).not.toBeNull();
        const chips = await screen.findByRole('group', { name: 'Tra cứu nhanh dữ liệu của bạn' });
        await click(within(chips).getByRole('button', { name: 'Gợi ý: Đặt lịch khám' }));
        for (const name of ['Nội khoa', 'Bác sĩ An', '02/01/2030', '08:00 – 08:30']) {
            await click(await screen.findByRole('button', { name }));
        }
        fireEvent.change(await screen.findByRole('textbox', { name: 'Lý do khám từ 10 đến 500 ký tự' }), { target: { value: 'Khám tổng quát' } });
        await click(screen.getByRole('button', { name: 'Xem lại thông tin khám' }));
        expect(await screen.findByText('Tóm tắt thông tin đặt lịch')).toBeInTheDocument();
        await click(screen.getByRole('button', { name: 'Xem tóm tắt thông tin khám' }));
        await click(await screen.findByRole('button', { name: 'Xác nhận đặt lịch' }));
        expect(await screen.findByText('BUBBLE-42')).toBeInTheDocument();
        expect(mocks.post.mock.calls.slice(0, 6).map(([, body]) => body.step)).toEqual(['start', 'pick', 'pick', 'pick', 'pick', 'reason']);
        expect(mocks.post).toHaveBeenLastCalledWith('/appointments', expect.objectContaining({
            doctorId: 2, appointmentSlotId: 3, confirmationId: 'bubble-confirmation',
        }), expect.objectContaining({ headers: { 'Idempotency-Key': expect.any(String) } }));
        expect(screen.getByLabelText('Địa chỉ').textContent).toBe('/before');
    });
});
