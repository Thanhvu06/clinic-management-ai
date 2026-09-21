import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider, useChatContext } from '../contexts/ChatContext';
import axiosClient from '../api/axiosClient';
import type { AiBookingDraft } from '../types/ai';

// Mock AuthContext
let mockUser: { id: string; fullName: string; role: string } | null = {
    id: 'pat-100',
    fullName: 'Nguyen Van Test',
    role: 'Patient'
};

vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({
        isAuthenticated: !!mockUser,
        user: mockUser,
        logout: vi.fn(),
    }),
}));

// Mock axiosClient
vi.mock('../api/axiosClient', () => ({
    default: {
        post: vi.fn(),
        get: vi.fn(),
        put: vi.fn(),
        delete: vi.fn(),
    }
}));

describe('AI Draft Version Management Contract & Flow', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        sessionStorage.clear();
        mockUser = { id: 'pat-100', fullName: 'Nguyen Van Test', role: 'Patient' };
    });

    const DraftVersionController: React.FC<{
        initialDraft?: AiBookingDraft;
    }> = ({ initialDraft }) => {
        const { activeDraft, setActiveDraft } = useChatContext();
        return (
            <div>
                <span data-testid="active-draft-version">{activeDraft?.version ?? 'none'}</span>
                <button
                    onClick={() => setActiveDraft(initialDraft || { specialtyId: 1, specialtyName: 'Nội tiết', version: 2, isComplete: false })}
                >
                    Set Draft Version
                </button>
            </div>
        );
    };

    it('Scenario 1: actionVersion = null with active draft v1 does NOT display vnull and blocks unsafe execution', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Chọn khung giờ khám:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, doctorId: 10, version: 1 },
                actions: [
                    {
                        id: 'act-null-ver',
                        type: 'SelectSlot',
                        label: '08:00 - 08:30 (BS Minh)',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        draftVersion: null as unknown as number,
                        payload: { specialtyId: 1, doctorId: 10, slotId: 101, draftVersion: null }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Khám buổi sáng' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText(/08:00 - 08:30/i)).toBeInTheDocument();
        });

        // Click action with null version
        fireEvent.click(screen.getByText(/08:00 - 08:30/i));

        await waitFor(() => {
            // Must NOT contain "vnull" anywhere
            expect(screen.queryByText(/vnull/i)).not.toBeInTheDocument();
            // Must show friendly notice and offer reload
            expect(screen.getByText(/Lựa chọn này được tạo từ phiên trò chuyện cũ hoặc chưa được đồng bộ phiên bản/i)).toBeInTheDocument();
            expect(screen.getByText('Tải lại lựa chọn hiện tại')).toBeInTheDocument();
        });
    });

    it('Scenario 2: actionVersion = undefined with active draft v1 does NOT display vundefined', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Chọn khung giờ:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, doctorId: 10, version: 1 },
                actions: [
                    {
                        id: 'act-undef-ver',
                        type: 'SelectSlot',
                        label: '09:00 - 09:30 (BS Minh)',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        draftVersion: undefined,
                        payload: { specialtyId: 1, doctorId: 10, slotId: 102 }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Khám 9 giờ' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText(/09:00 - 09:30/i)).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText(/09:00 - 09:30/i));

        await waitFor(() => {
            expect(screen.queryByText(/vundefined/i)).not.toBeInTheDocument();
            expect(screen.getByText(/Lựa chọn này được tạo từ phiên trò chuyện cũ hoặc chưa được đồng bộ phiên bản/i)).toBeInTheDocument();
        });
    });

    it('Scenario 3: actionVersion with NaN, negative, decimal, or string does NOT crash and never outputs invalid versions', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Chọn bác sĩ:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 2 },
                actions: [
                    {
                        id: 'act-bad-nan',
                        type: 'SelectDoctor',
                        label: 'BS NaN Version',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        draftVersion: NaN,
                        payload: { specialtyId: 1, doctorId: 12, draftVersion: NaN }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Tìm bác sĩ' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('BS NaN Version')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('BS NaN Version'));

        await waitFor(() => {
            expect(screen.queryByText(/vNaN/i)).not.toBeInTheDocument();
            expect(screen.queryByText(/vnull/i)).not.toBeInTheDocument();
            expect(screen.getByText(/Lựa chọn này được tạo từ phiên trò chuyện cũ hoặc chưa được đồng bộ phiên bản/i)).toBeInTheDocument();
        });
    });

    it('Scenario 4: Action v1 with active draft v2 is blocked, no booking API is called, and displays clean version warning', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Thông tin lịch khám:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 1 },
                actions: [
                    {
                        id: 'act-v1',
                        type: 'SelectDoctor',
                        label: 'Chọn BS Nguyễn Minh Khải (v1)',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        draftVersion: 1,
                        payload: { specialtyId: 1, doctorId: 10, draftVersion: 1 }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <DraftVersionController initialDraft={{ specialtyId: 1, specialtyName: 'Nội tiết', version: 2, isComplete: false }} />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Bác sĩ Khải' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Chọn BS Nguyễn Minh Khải (v1)')).toBeInTheDocument();
        });

        // Bump draft to v2
        fireEvent.click(screen.getByText('Set Draft Version'));

        // Click stale v1 action
        fireEvent.click(screen.getByText(/Chọn BS Nguyễn Minh Khải \(v1\)/i));

        await waitFor(() => {
            expect(screen.getByText(/Thao tác này thuộc phiên bản thảo lịch cũ \(v1\)\. Thông tin lịch khám hiện tại đã được cập nhật sang phiên bản mới hơn \(v2\)\./i)).toBeInTheDocument();
            // Did NOT call /appointments or send new /ai/chat for booking
            expect(axiosClient.post).toHaveBeenCalledTimes(1); // Only initial chat message
        });
    });

    it('Scenario 5: Action v2 with active draft v2 executes normally', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Xác nhận đặt lịch khám:',
                urgency: 'ROUTINE',
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim Mạch',
                    doctorId: 10,
                    doctorName: 'BS Nguyễn Minh Khải',
                    slotId: 101,
                    slotDate: '2026-09-25',
                    startTime: '08:00',
                    endTime: '08:30',
                    reason: 'Khám sức khỏe định kỳ tim mạch',
                    isComplete: true,
                    version: 2
                },
                actions: [
                    {
                        id: 'act-confirm-v2',
                        type: 'ConfirmBooking',
                        label: 'Xác nhận đặt khám v2',
                        style: 'primary',
                        requiresAuthentication: true,
                        requiresConfirmation: true,
                        draftVersion: 2,
                        payload: {
                            specialtyId: 1,
                            specialtyName: 'Tim Mạch',
                            doctorId: 10,
                            doctorName: 'BS Nguyễn Minh Khải',
                            slotId: 101,
                            slotDate: '2026-09-25',
                            startTime: '08:00',
                            endTime: '08:30',
                            reason: 'Khám sức khỏe định kỳ tim mạch',
                            draftVersion: 2
                        }
                    }
                ]
            }
        });

        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Appointment created',
            data: {
                id: 888,
                appointmentCode: 'APPT-20260925-888',
                status: 'Scheduled'
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Xác nhận thông tin v2' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Xác nhận đặt khám v2')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xác nhận đặt khám v2'));

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenCalledWith(
                '/appointments',
                expect.objectContaining({
                    doctorId: 10,
                    specialtyId: 1,
                    appointmentSlotId: 101,
                    reason: 'Khám sức khỏe định kỳ tim mạch'
                }),
                expect.any(Object)
            );
            expect(screen.getByText(/Đặt lịch khám thành công!/i)).toBeInTheDocument();
            expect(screen.getByText(/APPT-20260925-888/i)).toBeInTheDocument();
        });
    });

    it('Scenario 6: Stale action is styled with data-stale, aria-disabled and (Lựa chọn đã cũ) label', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Chọn bác sĩ:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 1 },
                actions: [
                    {
                        id: 'act-stale-ui',
                        type: 'SelectDoctor',
                        label: 'BS Minh Khải',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        draftVersion: 1,
                        payload: { specialtyId: 1, doctorId: 10, draftVersion: 1 }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <DraftVersionController initialDraft={{ specialtyId: 1, specialtyName: 'Nội tiết', version: 2, isComplete: false }} />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Khám bác sĩ Khải' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText(/BS Minh Khải/i)).toBeInTheDocument();
        });

        // Bump draft to v2
        fireEvent.click(screen.getByText('Set Draft Version'));

        await waitFor(() => {
            const btn = screen.getByText(/BS Minh Khải/i).closest('button');
            expect(btn).toHaveAttribute('data-stale', 'true');
            expect(btn).toHaveAttribute('aria-disabled', 'true');
            expect(screen.getByText('(Lựa chọn đã cũ)')).toBeInTheDocument();
        });
    });

    it('Scenario 7: Clicking stale action does not spam duplicate warnings and offers reload button', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Chọn ngày khám:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, doctorId: 10, version: 1 },
                actions: [
                    {
                        id: 'act-date-1',
                        type: 'ChangePreferredDate',
                        label: 'Ngày 25/09/2026',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        draftVersion: 1,
                        payload: { specialtyId: 1, doctorId: 10, slotDate: '2026-09-25', draftVersion: 1 }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <DraftVersionController initialDraft={{ specialtyId: 1, specialtyName: 'Nội tiết', version: 3, isComplete: false }} />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Chọn ngày' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText(/Ngày 25\/09\/2026/i)).toBeInTheDocument();
        });

        // Bump to v3
        fireEvent.click(screen.getByText('Set Draft Version'));

        // Click stale button twice
        const staleBtn = screen.getByText(/Ngày 25\/09\/2026/i).closest('button')!;
        fireEvent.click(staleBtn);
        fireEvent.click(staleBtn);

        await waitFor(() => {
            const warnings = screen.getAllByText(/Thao tác này thuộc phiên bản thảo lịch cũ \(v1\)/i);
            // Should avoid duplicate spam
            expect(warnings.length).toBe(1);
            expect(screen.getByText('Tải lại lựa chọn hiện tại')).toBeInTheDocument();
        });
    });

    it('Scenario 8: Outgoing /ai/chat request sends current active draftVersion', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Chào bạn, bạn đã có bản nháp v2.',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 2 },
                actions: []
            }
        });

        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Đã cập nhật theo phiên bản mới.',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 3 },
                actions: []
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Tin nhắn 1' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Chào bạn, bạn đã có bản nháp v2.')).toBeInTheDocument();
        });

        // Send 2nd message, it should include draftVersion: 2
        fireEvent.change(input, { target: { value: 'Tin nhắn 2' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenLastCalledWith(
                '/ai/chat',
                expect.objectContaining({
                    message: 'Tin nhắn 2',
                    draftVersion: 2
                }),
                expect.any(Object)
            );
        });
    });

    it('Scenario 9: Late response with older draft version does not overwrite newer active draft version', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Bản nháp v2 đã sẵn sàng.',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, specialtyName: 'Tim Mạch', version: 2 },
                actions: []
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <DraftVersionController initialDraft={{ specialtyId: 1, specialtyName: 'Nội tiết', version: 3, isComplete: false }} />
                </ChatProvider>
            </MemoryRouter>
        );

        // Manually set active draft to v3
        fireEvent.click(screen.getByText('Set Draft Version'));
        expect(screen.getByTestId('active-draft-version')).toHaveTextContent('3');

        // Now an incoming response arrives with v2 (older than v3)
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Lấy thông tin' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Bản nháp v2 đã sẵn sàng.')).toBeInTheDocument();
        });

        // Active draft version must NOT be downgraded to 2, it should remain 3
        expect(screen.getByTestId('active-draft-version')).toHaveTextContent('3');
    });

    it('Scenario 10: Clear chat resets active draft version and clears sessionStorage', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Bản nháp v1',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 1 },
                actions: []
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <DraftVersionController />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Bắt đầu' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByTestId('active-draft-version')).toHaveTextContent('1');
        });

        // Click Clear Chat button
        fireEvent.click(screen.getByLabelText('Làm mới cuộc trò chuyện'));

        await waitFor(() => {
            expect(screen.getByTestId('active-draft-version')).toHaveTextContent('none');
            expect(sessionStorage.getItem('cliniccare_booking_draft_pat-100')).toBeNull();
        });
    });

    it('Scenario 11: Double-click ConfirmBooking creates only 1 appointment request', async () => {
        let resolveBooking: (val: unknown) => void = () => {};
        const bookingPromise = new Promise(resolve => {
            resolveBooking = resolve;
        });

        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Xác nhận:',
                urgency: 'ROUTINE',
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim Mạch',
                    doctorId: 10,
                    doctorName: 'BS Khải',
                    slotId: 101,
                    slotDate: '2026-09-25',
                    startTime: '08:00',
                    endTime: '08:30',
                    reason: 'Khám sức khỏe tim mạch định kỳ',
                    isComplete: true,
                    version: 1
                },
                actions: [
                    {
                        id: 'act-confirm-double',
                        type: 'ConfirmBooking',
                        label: 'Xác nhận đặt lịch một lần',
                        style: 'primary',
                        requiresAuthentication: true,
                        requiresConfirmation: true,
                        draftVersion: 1,
                        payload: {
                            specialtyId: 1,
                            doctorId: 10,
                            slotId: 101,
                            slotDate: '2026-09-25',
                            startTime: '08:00',
                            endTime: '08:30',
                            reason: 'Khám sức khỏe tim mạch định kỳ',
                            draftVersion: 1
                        }
                    }
                ]
            }
        });

        vi.mocked(axiosClient.post).mockImplementation((url: string) => {
            if (url === '/appointments') {
                return bookingPromise as Promise<any>;
            }
            return Promise.resolve({ success: true, message: '', data: {} });
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Xác nhận ngay' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Xác nhận đặt lịch một lần')).toBeInTheDocument();
        });

        const btn = screen.getByText('Xác nhận đặt lịch một lần');
        fireEvent.click(btn);
        fireEvent.click(btn); // Second rapid click

        const appointmentCalls = vi.mocked(axiosClient.post).mock.calls.filter(c => c[0] === '/appointments');
        expect(appointmentCalls.length).toBe(1);

        resolveBooking({
            success: true,
            message: 'Booked',
            data: { id: 99, appointmentCode: 'APPT-99', status: 'Scheduled' }
        });

        await waitFor(() => {
            expect(screen.getByText(/Đặt lịch khám thành công!/i)).toBeInTheDocument();
        });
    });

    it('Scenario 12: Independent navigation actions (ContactReception, ViewBills) work regardless of draft version and never trigger stale warnings', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Dịch vụ hỗ trợ:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 1 },
                actions: [
                    {
                        id: 'act-contact',
                        type: 'ContactReception',
                        label: 'Liên hệ lễ tân',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        payload: {}
                    },
                    {
                        id: 'act-bills',
                        type: 'ViewBills',
                        label: 'Xem hóa đơn',
                        style: 'secondary',
                        requiresAuthentication: true,
                        requiresConfirmation: false,
                        payload: { targetUrl: '/patient/invoices' }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <DraftVersionController initialDraft={{ specialtyId: 1, specialtyName: 'Nội tiết', version: 5, isComplete: false }} />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Hỗ trợ lễ tân và hóa đơn' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Liên hệ lễ tân')).toBeInTheDocument();
            expect(screen.getByText('Xem hóa đơn')).toBeInTheDocument();
        });

        // Bump draft to v5
        fireEvent.click(screen.getByText('Set Draft Version'));

        // Neither button should be marked stale
        const contactBtn = screen.getByText('Liên hệ lễ tân').closest('button');
        const billsBtn = screen.getByText('Xem hóa đơn').closest('button');

        expect(contactBtn).not.toHaveAttribute('data-stale');
        expect(billsBtn).not.toHaveAttribute('data-stale');

        // Clicking ContactReception opens modal/info without stale warning
        fireEvent.click(screen.getByText('Liên hệ lễ tân'));

        await waitFor(() => {
            expect(screen.getByText(/Thông tin Quầy Tiếp Đón & Lễ Tân/i)).toBeInTheDocument();
            expect(screen.queryByText(/Thao tác này thuộc phiên bản thảo lịch cũ/i)).not.toBeInTheDocument();
        });
    });
});
