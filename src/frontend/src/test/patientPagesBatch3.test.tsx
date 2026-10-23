import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { ChatProvider } from '../contexts/ChatContext';

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), showAlert: vi.fn(), send: vi.fn(), clear: vi.fn(), flow: {} as Record<string, unknown>, menu: [] }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'batch3-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.showAlert }) }));
vi.mock('../components/copilot/useSuggestionMenu', async importOriginal => ({ ...await importOriginal<typeof import('../components/copilot/useSuggestionMenu')>(), useSuggestionMenu: () => mocks.menu }));
vi.mock('../hooks/useAiBookingFlow', async importOriginal => ({ ...await importOriginal<typeof import('../hooks/useAiBookingFlow')>(), useAiBookingFlow: () => mocks.flow }));

const specialty = { id: 1, specialtyCode: 'SP01', specialtyName: 'Nội khoa', description: 'Khám tổng quát', aiEnabled: true };
const doctor = { id: 2, fullName: 'Nguyễn Minh', specialtyId: 1, experienceYears: 12 };
const slot = { slotId: 10, doctorId: 2, slotDate: '2030-01-02', startTime: '08:00', endTime: '08:30' };
const ok = (data: unknown) => ({ success: true, data });
const defaultGet = async (url: string) => url === '/specialties' ? ok([specialty]) : url.includes('/available-slots') ? ok([slot]) : ok([doctor]);
const mount = (node: React.ReactNode = <BookAppointment />, route = '/patient/book') => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[route]}><ChatProvider><Routes>
    <Route path="/patient/book" element={node} /><Route path="/patient/ai-consultation" element={node} /><Route path="/patient/appointments" element={<p>Danh sách lịch hẹn</p>} />
</Routes></ChatProvider></MemoryRouter></ConfigProvider>);
const selectSpecialty = async () => {
    fireEvent.click(await screen.findByRole('button', { name: /^Nội khoa/ }));
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn bác sĩ/ }));
};
const selectDoctor = async () => {
    await selectSpecialty();
    fireEvent.click(await screen.findByRole('button', { name: /Nguyễn Minh/ }));
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn giờ khám/ }));
};
const review = async () => {
    await selectDoctor();
    fireEvent.change(screen.getByLabelText('Chọn ngày khám'), { target: { value: slot.slotDate } });
    fireEvent.click(await screen.findByRole('button', { name: '08:00' }));
    fireEvent.change(screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/), { target: { value: 'Khám sức khỏe định kỳ' } });
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/ }));
};
beforeEach(() => {
    vi.clearAllMocks(); mocks.get.mockReset(); mocks.post.mockReset(); localStorage.clear(); sessionStorage.clear();
    mocks.get.mockImplementation(defaultGet);
    mocks.post.mockResolvedValue(ok({ id: 42, appointmentCode: 'APT-B3', doctorName: doctor.fullName, specialtyName: specialty.specialtyName, startTime: '08:00', endTime: '08:30', reason: 'Khám sức khỏe định kỳ' }));
    mocks.flow = { input: '', setInput: vi.fn(), loading: false, submittingBooking: false, errorMsg: '', messages: [], activeDraft: null,
        clearChat: mocks.clear, handleSendMessage: mocks.send, handleSuggestion: vi.fn(), wizard: null, handleWizardStep: vi.fn(), handleActionClick: vi.fn(), formatVietnameseDate: vi.fn() };
});
afterEach(cleanup);

describe('BookAppointment batch 3', () => {
    it.each(['reject', 'success=false'])('B1 specialties %s replaces content with an alert and retry reloads the API', async mode => {
        mocks.get.mockImplementationOnce(() => mode === 'reject' ? Promise.reject(new Error('network')) : Promise.resolve({ success: false }));
        mount();
        expect(await screen.findByRole('alert')).toHaveTextContent('Không tải được danh sách chuyên khoa.');
        expect(screen.queryByText('Chưa có chuyên khoa để đặt lịch.')).not.toBeInTheDocument();
        expect(screen.getByText('Bước 1: Chọn chuyên khoa khám')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('button', { name: /^Nội khoa/ })).toBeInTheDocument();
        expect(mocks.get.mock.calls.filter(([url]) => url === '/specialties')).toHaveLength(2);
    });
    it('B1 empty specialties have a separate empty state without an error alert', async () => {
        mocks.get.mockResolvedValue(ok([])); mount();
        expect(await screen.findByText('Chưa có chuyên khoa để đặt lịch.')).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
    it('F2 a step 3 AI draft keeps the date, reason and slots when specialties fail', async () => {
        sessionStorage.setItem('cliniccare_booking_draft_batch3-patient', JSON.stringify({
            draftId: 'batch3-draft', specialtyId: 1, specialtyName: specialty.specialtyName,
            doctorId: doctor.id, doctorName: doctor.fullName, slotId: slot.slotId, slotDate: slot.slotDate,
            startTime: slot.startTime, endTime: slot.endTime, reason: 'Khám sức khỏe định kỳ', isComplete: true, version: 1
        }));
        mocks.get.mockImplementation((url: string) => url === '/specialties' ? Promise.reject(new Error('network')) : defaultGet(url));
        mount();
        const alert = await screen.findByRole('alert');
        expect(alert).toHaveTextContent('Không tải được danh sách chuyên khoa.');
        expect(alert).toHaveClass('ant-alert-warning');
        expect(within(alert).getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(screen.getByLabelText(/Triệu chứng hoặc lý do/)).toHaveValue('Khám sức khỏe định kỳ');
        expect(screen.getByLabelText('Chọn ngày khám')).toHaveAttribute('id', 'slotDate');
        expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '3');
        expect(await screen.findByRole('button', { name: '08:00' })).toHaveAttribute('aria-pressed', 'true');
    });
    it.each(['reject', 'success=false'])('B2 doctors %s shows retry instead of the empty state', async mode => {
        mocks.get.mockImplementation((url: string) => url.includes('/specialties/1/doctors') ? mode === 'reject' ? Promise.reject(new Error('network')) : Promise.resolve({ success: false }) : defaultGet(url));
        mount(); await selectSpecialty();
        expect(await screen.findByRole('alert')).toHaveTextContent('Không tải được danh sách bác sĩ.');
        expect(screen.queryByText('Chưa có bác sĩ thuộc chuyên khoa này')).not.toBeInTheDocument();
        const calls = mocks.get.mock.calls.length; mocks.get.mockImplementation(defaultGet);
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('button', { name: /Nguyễn Minh/ })).toBeInTheDocument();
        expect(mocks.get.mock.calls.slice(calls).some(([url]) => url === '/specialties/1/doctors')).toBe(true);
    });
    it('B2/B9 empty doctors use Empty and can return to specialties', async () => {
        mocks.get.mockImplementation((url: string) => url.includes('/doctors') ? Promise.resolve(ok([])) : defaultGet(url));
        mount(); await selectSpecialty();
        expect(await screen.findByText('Chưa có bác sĩ thuộc chuyên khoa này')).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /Chọn lại chuyên khoa/ }));
        expect(screen.getByRole('heading', { name: 'Bước 1: Chọn chuyên khoa khám' })).toBeInTheDocument();
    });
    it.each(['reject', 'success=false'])('B3 slots %s shows retry instead of no availability', async mode => {
        mocks.get.mockImplementation((url: string) => url.includes('/available-slots') ? mode === 'reject' ? Promise.reject(new Error('network')) : Promise.resolve({ success: false }) : defaultGet(url));
        mount(); await selectDoctor();
        expect(await screen.findByRole('alert')).toHaveTextContent('Không tải được khung giờ khám.');
        expect(screen.queryByText(/Bác sĩ chưa có lịch trống/)).not.toBeInTheDocument();
        const calls = mocks.get.mock.calls.length; mocks.get.mockImplementation(defaultGet);
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('button', { name: '08:00' })).toBeInTheDocument();
        expect(mocks.get.mock.calls.slice(calls).some(([url]) => url.includes('/available-slots'))).toBe(true);
    });
    it('B3 empty slots differ from errors', async () => {
        mocks.get.mockImplementation((url: string) => url.includes('/available-slots') ? Promise.resolve(ok([])) : defaultGet(url));
        mount(); await selectDoctor();
        expect(await screen.findByText(/Bác sĩ chưa có lịch trống/)).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Thử lại' })).not.toBeInTheDocument();
    });
    it('B4 submitting changes the button text, disables both controls and has no loading class', async () => {
        mocks.post.mockImplementation(() => new Promise(() => {})); mount(); await review();
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận & đặt lịch' }));
        const submit = await screen.findByRole('button', { name: 'Đang đặt lịch...' });
        expect(submit).toBeDisabled(); expect(submit).not.toHaveClass('ant-btn-loading');
        expect(screen.getByRole('button', { name: /Quay lại/ })).toBeDisabled();
        fireEvent.click(submit); expect(mocks.post).toHaveBeenCalledTimes(1);
    });
    it('B5 success shows all five saved fields, the notice and reset action', async () => {
        mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Xác nhận & đặt lịch' }));
        await screen.findByText('Đặt lịch khám thành công!');
        for (const label of ['Mã lịch hẹn:', 'Chuyên khoa:', 'Bác sĩ khám:', 'Thời gian hẹn:', 'Lý do khám:']) expect(screen.getByText(label)).toBeInTheDocument();
        expect(screen.getAllByText('#APT-B3')).toHaveLength(2);
        expect(screen.getByText('08:00 - 08:30 (02/01/2030)')).toBeInTheDocument();
        expect(screen.getByText('"Khám sức khỏe định kỳ"')).toBeInTheDocument();
        expect(screen.getByRole('alert')).toHaveTextContent('trước giờ hẹn 10–15 phút');
        fireEvent.click(screen.getByRole('button', { name: 'Đặt thêm lịch hẹn khác' }));
        expect(await screen.findByRole('heading', { name: 'Bước 1: Chọn chuyên khoa khám' })).toBeInTheDocument();
        expect(screen.getAllByText('Chưa chọn')).toHaveLength(3);
    });
    it('B5 success navigates to my appointments', async () => {
        mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Xác nhận & đặt lịch' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Xem lịch hẹn của tôi' }));
        expect(screen.getByText('Danh sách lịch hẹn')).toBeInTheDocument();
    });
    it('B6 loading replaces all step content with a Card and Spin', () => {
        mocks.get.mockImplementation(() => new Promise(() => {})); const { container } = mount();
        expect(screen.getByText('Đang tải dữ liệu phòng khám...').closest('.ant-card')).not.toBeNull();
        expect(container.querySelector('.ant-spin')).not.toBeNull();
        expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
        expect(screen.queryByText('Chưa có chuyên khoa để đặt lịch.')).not.toBeInTheDocument();
    });
    it('B7 revisit remains a status banner and Sunday has a warning instead of slots', async () => {
        mount(<BookAppointment />, '/patient/book?revisitRequestId=7');
        expect(await screen.findByRole('status')).toHaveTextContent('đề xuất tái khám');
        await selectDoctor(); const calls = mocks.get.mock.calls.length;
        fireEvent.change(screen.getByLabelText('Chọn ngày khám'), { target: { value: '2030-01-06' } });
        expect(screen.getByRole('alert')).toHaveTextContent('Phòng khám không mở lịch bác sĩ vào Chủ nhật.');
        expect(screen.queryByRole('button', { name: '08:00' })).not.toBeInTheDocument();
        await act(async () => {});
        expect(mocks.get.mock.calls.slice(calls).some(([url]) => url.includes('/available-slots'))).toBe(false);
    });
    it('B7 uncertain attempt preserves status/actions and hides the technical term', async () => {
        mocks.post.mockRejectedValue(new Error('Gián đoạn kết nối')); mount(); await review();
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận & đặt lịch' }));
        const banner = await screen.findByRole('status');
        expect(banner).toHaveTextContent('Lượt đặt lịch trước đó đang chưa rõ kết quả');
        expect(banner).not.toHaveTextContent('Idempotency-Key');
        expect(within(banner).getByRole('button', { name: 'Đối soát lịch hẹn đã tạo' })).toBeInTheDocument();
        fireEvent.click(within(banner).getByRole('button', { name: 'Hủy & bắt đầu lượt đặt lịch mới' }));
        expect(screen.getByRole('heading', { name: 'Bước 1: Chọn chuyên khoa khám' })).toBeInTheDocument();
    });
    it('B8 progressbar tracks each of the four Steps and keeps its bounds', async () => {
        const { container } = mount(); await screen.findByRole('button', { name: /^Nội khoa/ });
        expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '1');
        expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuemin', '1'); expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuemax', '4');
        expect(container.querySelector('.ant-steps')).not.toBeNull();
        await selectSpecialty(); expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '2');
        fireEvent.click(await screen.findByRole('button', { name: /Nguyễn Minh/ })); fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn giờ khám/ }));
        expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '3');
        fireEvent.click(await screen.findByRole('button', { name: '08:00' })); fireEvent.change(screen.getByLabelText(/Triệu chứng hoặc lý do/), { target: { value: 'Khám tổng quát' } });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/ })); expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '4');
    });
    it('B9 specialty and doctor Cards retain keyboard selection, pressed state, Tag and Avatar', async () => {
        mount(); const spec = await screen.findByRole('button', { name: /^Nội khoa/ }); expect(spec).toHaveAttribute('tabindex', '0'); expect(spec).toHaveAttribute('aria-pressed', 'false');
        expect(within(spec).getByText('Hỗ trợ AI').closest('.ant-tag')).not.toBeNull();
        fireEvent.keyDown(spec, { key: 'Enter' }); expect(spec).toHaveAttribute('aria-pressed', 'true');
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn bác sĩ/ }));
        const doc = await screen.findByRole('button', { name: /Nguyễn Minh/ }); expect(doc.querySelector('.ant-avatar')).not.toBeNull();
        fireEvent.keyDown(doc, { key: ' ' }); expect(doc).toHaveAttribute('aria-pressed', 'true');
    });
    it('B10 slots are antd Buttons in the grid and expose selection', async () => {
        mount(); await selectDoctor(); const button = await screen.findByRole('button', { name: '08:00' });
        expect(button).toHaveClass('ant-btn'); expect(button.parentElement?.className).toMatch(/slotGrid/);
        expect(button).toHaveAttribute('aria-pressed', 'false'); fireEvent.click(button);
        expect(button).toHaveAttribute('aria-pressed', 'true'); expect(button).toHaveClass('ant-btn-primary');
    });
    it('B11 native date/reason IDs, min, character help and 10/500 validation stay intact', async () => {
        mount(); await selectDoctor(); const date = screen.getByLabelText('Chọn ngày khám'); const reason = screen.getByLabelText(/Triệu chứng hoặc lý do/);
        expect(date).toHaveAttribute('id', 'slotDate'); expect(date).toHaveAttribute('type', 'date'); expect(date).toHaveAttribute('min'); expect(reason).toHaveAttribute('id', 'reason');
        fireEvent.change(reason, { target: { value: 'Đau' } }); expect(await screen.findByText('Lý do khám phải có tối thiểu 10 ký tự.')).toBeInTheDocument();
        expect(screen.getByText('Tối thiểu 10 ký tự, tối đa 500 ký tự. (3/500 ký tự)')).toBeInTheDocument();
        fireEvent.change(reason, { target: { value: 'a'.repeat(501) } }); expect(await screen.findByText('Lý do khám vượt quá 500 ký tự.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/ })).toBeDisabled();
    });
    it('B12 review has five bordered description rows and no promise Alert', async () => {
        const { container } = mount(); await review(); const panel = container.querySelector('[class*="reviewPanel"]') as HTMLElement;
        expect(panel).toHaveClass('ant-descriptions-bordered');
        for (const label of ['Chuyên khoa', 'Bác sĩ phụ trách', 'Thời gian khám', 'Ngày', 'Triệu chứng/Ghi chú']) expect(within(panel).getByText(label)).toBeInTheDocument();
        expect(within(panel).getByText('02/01/2030')).toBeInTheDocument(); expect(within(panel).getByText('"Khám sức khỏe định kỳ"')).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(screen.queryByText(/cam kết/)).not.toBeInTheDocument();
    });
    it('B13 summary stays in a Card with unchanged empty copy and AI link', async () => {
        const { container } = mount(); await screen.findByRole('button', { name: /^Nội khoa/ });
        const summary = container.querySelector('aside .ant-card') as HTMLElement;
        expect(within(summary).getByText('Tóm tắt lịch khám')).toBeInTheDocument(); expect(within(summary).getAllByText('Chưa chọn')).toHaveLength(3);
        expect(screen.getByRole('link', { name: 'Hỏi trợ lý AI' })).toHaveAttribute('href', '/patient/ai-consultation');
    });
});
