import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider } from '../contexts/ChatContext';
import axiosClient from '../api/axiosClient';

// Mock AuthContext
let mockUser: { id: string; fullName: string; role: string } | null = {
    id: 'pat-1',
    fullName: 'Nguyen Van Patient',
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

describe('AI Action Assistant - Frontend Widget & Flow', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        sessionStorage.clear();
        mockUser = { id: 'pat-1', fullName: 'Nguyen Van Patient', role: 'Patient' };
    });

    it('does not render launcher if user is not a Patient', () => {
        mockUser = { id: 'doc-1', fullName: 'Dr. Strange', role: 'Doctor' };

        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        expect(screen.queryByLabelText('Mở Trợ lý ClinicCare AI')).not.toBeInTheDocument();
    });

    it('renders launcher button when user is a Patient', () => {
        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        expect(screen.getByLabelText('Mở Trợ lý ClinicCare AI')).toBeInTheDocument();
    });

    it('opens chat window with disclaimer and quick prompts when launcher clicked', async () => {
        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));

        expect(screen.getByRole('dialog', { name: /ClinicCare AI/i })).toBeInTheDocument();
        expect(screen.getByText(/không thay thế chẩn đoán y khoa/i)).toBeInTheDocument();
        expect(screen.getByText('Tôi nên khám chuyên khoa nào?')).toBeInTheDocument();
        expect(screen.getByText('Tìm lịch khám sớm nhất.')).toBeInTheDocument();
    });

    it('sends chat request when quick prompt is clicked and renders specialty suggestions', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Dựa trên mô tả của bạn, tôi gợi ý chuyên khoa Tim Mạch.',
                urgency: 'ROUTINE',
                specialtySuggestions: [
                    {
                        specialtyId: 1,
                        specialtyCode: 'SP-01',
                        specialtyName: 'Tim Mạch',
                        reason: 'Chuyên khoa điều trị các bệnh lý liên quan đến tim và mạch máu.'
                    }
                ],
                actions: [
                    {
                        id: 'act-view-sp',
                        type: 'ViewSpecialty',
                        label: 'Xem chi tiết khoa Tim Mạch',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        payload: { specialtyId: 1, specialtyCode: 'SP-01', specialtyName: 'Tim Mạch' }
                    }
                ]
            }
        });

        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        fireEvent.click(screen.getByText('Tôi nên khám chuyên khoa nào?'));

        await waitFor(() => {
            expect(screen.getByText(/Dựa trên mô tả của bạn, tôi gợi ý chuyên khoa Tim Mạch/i)).toBeInTheDocument();
            expect(screen.getByText('Phù hợp tham khảo')).toBeInTheDocument();
            expect(screen.getByText('Xem chi tiết khoa Tim Mạch')).toBeInTheDocument();
        });
    });

    it('renders emergency card with 115 call button when urgency is EMERGENCY', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Dấu hiệu bạn mô tả có thể là tình huống khẩn cấp!',
                urgency: 'EMERGENCY',
                safetyNotice: 'TÌNH HUỐNG CẤP CỨU: Vui lòng gọi 115 hoặc tới cơ sở y tế gần nhất!',
                actions: [
                    {
                        id: 'act-call-115',
                        type: 'CallEmergency',
                        label: 'Gọi Cấp cứu 115',
                        style: 'danger',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        payload: { targetUrl: 'tel:115' }
                    }
                ]
            }
        });

        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));

        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Tôi bị đau thắt ngực dữ dội kèm khó thở' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('CẢNH BÁO NGUY HIỂM')).toBeInTheDocument();
            expect(screen.getByText('Gọi Cấp cứu 115 ngay')).toBeInTheDocument();
            const telLink = screen.getByText('Gọi Cấp cứu 115 ngay').closest('a');
            expect(telLink).toHaveAttribute('href', 'tel:115');
        });
    });

    it('renders booking summary card and confirms appointment on user click', async () => {
        // AI returns booking draft and ConfirmBooking action
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Tôi đã chuẩn bị xong thông tin đặt khám cho bạn. Bạn vui lòng kiểm tra và xác nhận:',
                urgency: 'ROUTINE',
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim Mạch',
                    doctorId: 10,
                    doctorName: 'BS. CKII Nguyễn Văn B',
                    slotId: 101,
                    slotDate: '2026-09-15',
                    startTime: '08:00',
                    endTime: '08:30',
                    reason: 'Khám định kỳ tim mạch',
                    isComplete: true
                },
                actions: [
                    {
                        id: 'act-confirm',
                        type: 'ConfirmBooking',
                        label: 'Xác nhận đặt lịch ngay',
                        style: 'primary',
                        requiresAuthentication: true,
                        requiresConfirmation: true,
                        payload: {
                            specialtyId: 1,
                            specialtyName: 'Tim Mạch',
                            doctorId: 10,
                            doctorName: 'BS. CKII Nguyễn Văn B',
                            slotId: 101,
                            slotDate: '2026-09-15',
                            startTime: '08:00',
                            endTime: '08:30',
                            reason: 'Khám định kỳ tim mạch'
                        }
                    }
                ]
            }
        });

        // Booking creation success mock
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Appointment created',
            data: {
                id: 501,
                appointmentCode: 'APPT-20260915-001',
                status: 'Scheduled'
            }
        });

        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Tôi đồng ý với lịch này' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Tóm tắt thông tin đặt lịch')).toBeInTheDocument();
            expect(screen.getByText('BS. CKII Nguyễn Văn B')).toBeInTheDocument();
            expect(screen.getByText('08:00 - 08:30')).toBeInTheDocument();
            expect(screen.getByText('Xác nhận đặt lịch ngay')).toBeInTheDocument();
        });

        // Click Confirm Booking
        fireEvent.click(screen.getByText('Xác nhận đặt lịch ngay'));

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenCalledWith('/appointments', {
                doctorId: 10,
                specialtyId: 1,
                appointmentSlotId: 101,
                reason: 'Khám định kỳ tim mạch'
            });
            expect(screen.getByText(/Đặt lịch khám thành công!/i)).toBeInTheDocument();
            expect(screen.getByText(/APPT-20260915-001/i)).toBeInTheDocument();
        });
    });

    it('handles 409 slot conflict during booking confirmation gracefully', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Vui lòng xác nhận lịch:',
                urgency: 'ROUTINE',
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim Mạch',
                    doctorId: 10,
                    doctorName: 'BS. CKII Nguyễn Văn B',
                    slotId: 102,
                    slotDate: '2026-09-15',
                    startTime: '09:00',
                    endTime: '09:30',
                    isComplete: true
                },
                actions: [
                    {
                        id: 'act-confirm-2',
                        type: 'ConfirmBooking',
                        label: 'Xác nhận đặt lịch',
                        style: 'primary',
                        requiresAuthentication: true,
                        requiresConfirmation: true,
                        payload: {
                            specialtyId: 1,
                            specialtyName: 'Tim Mạch',
                            doctorId: 10,
                            doctorName: 'BS. CKII Nguyễn Văn B',
                            slotId: 102,
                            slotDate: '2026-09-15',
                            startTime: '09:00',
                            endTime: '09:30'
                        }
                    }
                ]
            }
        });

        // Booking fails with SLOT_ALREADY_BOOKED
        vi.mocked(axiosClient.post).mockRejectedValueOnce({
            errorCode: 'SLOT_ALREADY_BOOKED',
            response: {
                status: 409,
                data: {
                    success: false,
                    errorCode: 'SLOT_ALREADY_BOOKED',
                    message: 'Khung giờ này đã được đặt.'
                }
            }
        });

        // Re-query AI mock
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Dưới đây là các khung giờ khác còn trống của bác sĩ Nguyễn Văn B:',
                urgency: 'ROUTINE',
                actions: []
            }
        });

        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Đặt khung giờ 09:00' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Xác nhận đặt lịch')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xác nhận đặt lịch'));

        await waitFor(() => {
            expect(screen.getByText(/vừa có bệnh nhân khác đặt trước/i)).toBeInTheDocument();
        });
    });
});
