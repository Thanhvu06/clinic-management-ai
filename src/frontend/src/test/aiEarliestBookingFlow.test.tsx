import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ChatProvider, useChatContext } from '../contexts/ChatContext';
import { DialogProvider } from '../contexts/DialogContext';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { BookAppointment } from '../pages/patient/BookAppointment';
import axiosClient from '../api/axiosClient';
import type { AiBookingDraft } from '../types/ai';

const mockUser = {
    id: 41,
    userId: 'patient-account-41',
    fullName: 'Patient Test',
    role: 'Patient'
};

vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({
        isAuthenticated: true,
        user: mockUser,
        loading: false,
        logout: vi.fn()
    })
}));

vi.mock('../api/axiosClient', () => ({
    default: {
        post: vi.fn(),
        get: vi.fn(),
        put: vi.fn(),
        delete: vi.fn()
    }
}));

const specialties = [
    { id: 1, specialtyCode: 'SP06', specialtyName: 'Tim mạch', description: 'Tim mạch', aiEnabled: true },
    { id: 2, specialtyCode: 'SP08', specialtyName: 'Thần kinh', description: 'Thần kinh', aiEnabled: true }
];

const doctors = [
    { id: 11, fullName: 'Nguyễn An', academicTitle: 'BS', specialtyId: 1 },
    { id: 22, fullName: 'Trần Bình', academicTitle: 'BS', specialtyId: 2 }
];

const slots = [
    { slotId: 101, doctorId: 11, slotDate: '2026-09-15', startTime: '08:00', endTime: '08:30' },
    { slotId: 202, doctorId: 22, slotDate: '2026-09-16', startTime: '09:00', endTime: '09:30' }
];

const apiSuccess = <T,>(data: T) => ({ success: true, message: '', data });

const finderResponse = apiSuccess({
    message: 'Mình đang dùng chuyên khoa Tim mạch bạn vừa chọn. Dưới đây là lịch trống lấy trực tiếp từ hệ thống.',
    urgency: 'ROUTINE' as const,
    actions: [
        {
            id: 'act-select-slot-101',
            type: 'SelectSlot' as const,
            label: '08:00 - 15/09 · BS Nguyễn An',
            style: 'primary' as const,
            requiresAuthentication: false,
            requiresConfirmation: false,
            payload: {
                specialtyId: 1,
                specialtyName: 'Tim mạch',
                doctorId: 11,
                doctorName: 'BS Nguyễn An',
                slotId: 101,
                slotDate: '2026-09-15',
                startTime: '08:00',
                endTime: '08:30'
            }
        }
    ],
    bookingDraft: {
        specialtyId: 1,
        specialtyName: 'Tim mạch',
        slotDate: '2026-09-15',
        isComplete: false
    },
    missingFields: ['Doctor', 'TimeSlot', 'Reason']
});

const ContextProbe = () => {
    const { activeDraft, setActiveDraft, messages } = useChatContext();
    return (
        <div>
            <output data-testid="draft-json">{JSON.stringify(activeDraft)}</output>
            <output data-testid="message-count">{messages.length}</output>
            <button
                type="button"
                onClick={() => setActiveDraft({
                    specialtyId: 1,
                    specialtyName: 'Tim mạch',
                    slotDate: '2026-09-15',
                    isComplete: false
                })}
            >
                Draft Tim mạch
            </button>
            <button
                type="button"
                onClick={() => setActiveDraft(previous => ({
                    specialtyId: 2,
                    specialtyName: 'Thần kinh',
                    slotDate: '2026-09-16',
                    reason: previous?.reason,
                    isComplete: false
                }))}
            >
                Draft Thần kinh
            </button>
        </div>
    );
};

const renderBookingPage = () => render(
    <MemoryRouter initialEntries={['/patient/book']}>
        <DialogProvider>
            <ChatProvider>
                <BookAppointment />
                <MedicalChatWidget />
                <ContextProbe />
            </ChatProvider>
        </DialogProvider>
    </MemoryRouter>
);

const renderWidgetWithProbe = () => render(
    <MemoryRouter initialEntries={['/patient/book']}>
        <ChatProvider>
            <MedicalChatWidget />
            <ContextProbe />
        </ChatProvider>
    </MemoryRouter>
);

const setupBookingGets = () => {
    vi.mocked(axiosClient.get).mockImplementation((url: string) => {
        if (url === '/specialties') return Promise.resolve(apiSuccess(specialties));
        if (url === '/specialties/1/doctors') return Promise.resolve(apiSuccess([doctors[0]]));
        if (url === '/specialties/2/doctors') return Promise.resolve(apiSuccess([doctors[1]]));
        if (url.includes('/doctors/11/available-slots')) return Promise.resolve(apiSuccess([slots[0]]));
        if (url.includes('/doctors/22/available-slots')) return Promise.resolve(apiSuccess([slots[1]]));
        return Promise.resolve(apiSuccess([]));
    });
};

describe('P0-1 earliest-slot booking context', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        sessionStorage.clear();
        setupBookingGets();
    });

    it('sends the structured intent without invented context and renders clear guidance when no specialty is selected', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce(apiSuccess({
            message: 'Bạn chưa chọn chuyên khoa. Vui lòng chọn chuyên khoa hoặc mô tả triệu chứng để hệ thống tìm lịch phù hợp.',
            urgency: 'ROUTINE' as const,
            actions: [],
            missingFields: ['Specialty', 'Doctor', 'TimeSlot', 'Reason']
        }));
        renderWidgetWithProbe();

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        fireEvent.click(screen.getByText('Tìm lịch khám sớm nhất.'));

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenCalledWith(
                '/ai/chat',
                expect.objectContaining({
                    intent: 'FindEarliestAvailableSlot',
                    pendingSpecialtyId: undefined,
                    pendingDoctorId: undefined,
                    pendingSlotId: undefined
                }),
                expect.objectContaining({ signal: expect.any(Object) })
            );
        });
        expect(await screen.findByText(/chưa chọn chuyên khoa/i)).toBeInTheDocument();
        expect(screen.queryByText(/Xác nhận đặt lịch khám/i)).not.toBeInTheDocument();
    });

    it('syncs page specialty into chat and sends structured intent with current context', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce(finderResponse);
        renderBookingPage();

        fireEvent.click(await screen.findByText('Tim mạch', { selector: 'h4' }));
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        fireEvent.click(screen.getByText('Tìm lịch khám sớm nhất.'));

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenCalledWith(
                '/ai/chat',
                expect.objectContaining({
                    message: 'Tìm lịch khám sớm nhất.',
                    intent: 'FindEarliestAvailableSlot',
                    pendingSpecialtyId: 1,
                    pendingSlotDate: expect.any(String),
                    reason: ''
                }),
                expect.objectContaining({ signal: expect.any(Object) })
            );
        });

        expect(await screen.findByText(/đang dùng chuyên khoa Tim mạch/i)).toBeInTheDocument();
        expect(screen.getByText('08:00 - 15/09 · BS Nguyễn An')).toBeInTheDocument();
        expect(screen.queryByText('Mở trang Đặt lịch khám')).not.toBeInTheDocument();
    });

    it('renders grounded slot, syncs chat selection back to the page, and never posts an appointment', async () => {
        vi.mocked(axiosClient.post)
            .mockResolvedValueOnce(finderResponse)
            .mockResolvedValueOnce(apiSuccess({
                message: 'Đã ghi nhận khung giờ. Vui lòng bổ sung lý do khám từ 10 đến 500 ký tự.',
                urgency: 'ROUTINE' as const,
                actions: [],
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim mạch',
                    doctorId: 11,
                    doctorName: 'BS Nguyễn An',
                    slotId: 101,
                    slotDate: '2026-09-15',
                    startTime: '08:00',
                    endTime: '08:30',
                    isComplete: false
                },
                missingFields: ['Reason']
            }));

        renderBookingPage();
        fireEvent.click(await screen.findByText('Tim mạch', { selector: 'h4' }));
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        fireEvent.click(screen.getByText('Tìm lịch khám sớm nhất.'));
        fireEvent.click(await screen.findByText('08:00 - 15/09 · BS Nguyễn An'));

        expect(await screen.findByText('Bước 3: Chọn ngày & giờ khám')).toBeInTheDocument();
        await waitFor(() => {
            expect(screen.getByText('08:00').closest('button')?.className).toContain('slotBtnActive');
        });
        const chatRequests = vi.mocked(axiosClient.post).mock.calls.filter(call => call[0] === '/ai/chat');
        expect(chatRequests[1]?.[1]).toEqual(expect.objectContaining({
            pendingSpecialtyId: 1,
            pendingDoctorId: 11,
            pendingSlotId: 101,
            pendingSlotDate: '2026-09-15'
        }));
        expect(screen.queryByText(/Xác nhận đặt lịch khám/i)).not.toBeInTheDocument();
        expect(vi.mocked(axiosClient.post).mock.calls.some(call => call[0] === '/appointments')).toBe(false);
    });

    it('clears dependent doctor and slot when specialty changes but preserves the reason', async () => {
        renderBookingPage();
        fireEvent.click(await screen.findByText('Tim mạch', { selector: 'h4' }));
        fireEvent.click(screen.getByText(/Tiếp tục: Chọn bác sĩ/i));
        fireEvent.click(await screen.findByText('BS. Nguyễn An'));
        fireEvent.click(screen.getByText(/Tiếp tục: Chọn giờ khám/i));
        fireEvent.change(await screen.findByLabelText('Triệu chứng hoặc lý do thăm khám (Không bắt buộc)'), {
            target: { value: 'Đau ngực khi vận động kéo dài' }
        });
        fireEvent.click(await screen.findByText('08:00'));
        fireEvent.click(screen.getByText('Quay lại'));
        fireEvent.click(screen.getByText('Quay lại'));
        fireEvent.click(screen.getByText('Thần kinh', { selector: 'h4' }));

        await waitFor(() => {
            const draft = JSON.parse(screen.getByTestId('draft-json').textContent || '{}') as AiBookingDraft;
            expect(draft.specialtyId).toBe(2);
            expect(draft.doctorId).toBeUndefined();
            expect(draft.slotId).toBeUndefined();
            expect(draft.reason).toBe('Đau ngực khi vận động kéo dài');
        });
    });

    it('ignores a late response after the user changes specialty', async () => {
        let resolveRequest: ((value: typeof finderResponse) => void) | undefined;
        vi.mocked(axiosClient.post).mockImplementationOnce(() => new Promise(resolve => {
            resolveRequest = resolve;
        }));
        renderWidgetWithProbe();

        fireEvent.click(screen.getByText('Draft Tim mạch'));
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        fireEvent.click(screen.getByText('Tìm lịch khám sớm nhất.'));
        fireEvent.click(screen.getByText('Draft Thần kinh'));
        resolveRequest?.(finderResponse);

        await waitFor(() => {
            const draft = JSON.parse(screen.getByTestId('draft-json').textContent || '{}') as AiBookingDraft;
            expect(draft.specialtyId).toBe(2);
        });
        expect(screen.queryByText(/đang dùng chuyên khoa Tim mạch/i)).not.toBeInTheDocument();
        expect(screen.getByTestId('message-count')).toHaveTextContent('2');
    });

    it('preserves grounded alternatives and reason after SLOT_ALREADY_BOOKED without auto-booking another slot', async () => {
        const validReason = 'Đau ngực khi vận động kéo dài';
        vi.mocked(axiosClient.post)
            .mockResolvedValueOnce(apiSuccess({
                message: 'Vui lòng xác nhận lịch đã chọn.',
                urgency: 'ROUTINE' as const,
                actions: [{
                    id: 'confirm-101',
                    type: 'ConfirmBooking' as const,
                    label: 'Xác nhận đặt lịch khám',
                    style: 'primary' as const,
                    requiresAuthentication: true,
                    requiresConfirmation: true,
                    payload: {
                        specialtyId: 1,
                        specialtyName: 'Tim mạch',
                        doctorId: 11,
                        doctorName: 'BS Nguyễn An',
                        slotId: 101,
                        slotDate: '2026-09-15',
                        startTime: '08:00',
                        endTime: '08:30',
                        reason: validReason
                    }
                }],
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim mạch',
                    doctorId: 11,
                    doctorName: 'BS Nguyễn An',
                    slotId: 101,
                    slotDate: '2026-09-15',
                    startTime: '08:00',
                    endTime: '08:30',
                    reason: validReason,
                    isComplete: true
                }
            }))
            .mockRejectedValueOnce({ errorCode: 'SLOT_ALREADY_BOOKED', message: 'Slot đã được đặt' })
            .mockResolvedValueOnce(apiSuccess({
                message: 'Đây là khung giờ thay thế còn trống.',
                urgency: 'ROUTINE' as const,
                actions: [{
                    ...finderResponse.data.actions[0],
                    id: 'act-select-slot-102',
                    label: '08:30 - 15/09 · BS Nguyễn An',
                    payload: {
                        ...finderResponse.data.actions[0].payload,
                        slotId: 102,
                        startTime: '08:30',
                        endTime: '09:00',
                        reason: validReason
                    }
                }],
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim mạch',
                    doctorId: 11,
                    doctorName: 'BS Nguyễn An',
                    slotDate: '2026-09-15',
                    reason: validReason,
                    isComplete: false
                }
            }));

        renderWidgetWithProbe();
        fireEvent.click(screen.getByText('Draft Tim mạch'));
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: validReason } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));
        fireEvent.click(await screen.findByText('Xác nhận đặt lịch khám'));

        expect(await screen.findByText(/vừa có bệnh nhân khác đặt trước/i)).toBeInTheDocument();
        expect(await screen.findByText('08:30 - 15/09 · BS Nguyễn An')).toBeInTheDocument();
        const draft = JSON.parse(screen.getByTestId('draft-json').textContent || '{}') as AiBookingDraft;
        expect(draft.reason).toBe(validReason);
        expect(draft.slotId).toBeUndefined();
        const appointmentCalls = vi.mocked(axiosClient.post).mock.calls.filter(call => call[0] === '/appointments');
        expect(appointmentCalls).toHaveLength(1);
        const alternativeRequest = vi.mocked(axiosClient.post).mock.calls.find((call, index) =>
            index > 0 && call[0] === '/ai/chat'
        );
        expect(alternativeRequest?.[1]).toEqual(expect.objectContaining({
            intent: 'FindEarliestAvailableSlot',
            pendingSpecialtyId: 1,
            pendingDoctorId: 11,
            pendingSlotDate: '2026-09-15',
            reason: validReason
        }));
    });
});
