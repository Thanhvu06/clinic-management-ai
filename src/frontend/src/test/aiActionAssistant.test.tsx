import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider, useChatContext } from '../contexts/ChatContext';
import { DialogProvider } from '../contexts/DialogContext';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { SafeMarkdown } from '../components/SafeMarkdown';
import { formatVietnameseDate, isSafeClientRoute } from '../hooks/useAiBookingFlow';
import axiosClient from '../api/axiosClient';

const LocationDisplay = () => {
    const location = useLocation();
    return <div data-testid="location-display">{location.pathname}</div>;
};

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
        vi.mocked(axiosClient.post).mockReset();
        vi.mocked(axiosClient.get).mockReset();
        vi.mocked(axiosClient.put).mockReset();
        vi.mocked(axiosClient.delete).mockReset();
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
            expect(axiosClient.post).toHaveBeenCalledWith(
                '/appointments',
                {
                    doctorId: 10,
                    specialtyId: 1,
                    appointmentSlotId: 101,
                    reason: 'Khám định kỳ tim mạch'
                },
                expect.objectContaining({
                    headers: expect.objectContaining({
                        'Idempotency-Key': expect.any(String)
                    })
                })
            );
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
                    reason: 'Khám định kỳ tim mạch',
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
                            endTime: '09:30',
                            reason: 'Khám định kỳ tim mạch'
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

    it('formats Vietnamese date correctly', () => {
        expect(formatVietnameseDate('2026-09-15')).toBe('15/09/2026');
        expect(formatVietnameseDate('2026-01-05')).toBe('05/01/2026');
        expect(formatVietnameseDate('')).toBe('');
    });

    it('navigates to /patient/invoices when ViewBills action is clicked', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Bạn có thể xem hóa đơn tại đây:',
                urgency: 'ROUTINE',
                actions: [
                    {
                        id: 'act-bills',
                        type: 'ViewBills',
                        label: 'Xem hóa đơn viện phí',
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
                    <LocationDisplay />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Tôi muốn xem hóa đơn' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Xem hóa đơn viện phí')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xem hóa đơn viện phí'));

        await waitFor(() => {
            expect(screen.getByTestId('location-display').textContent).toBe('/patient/invoices');
        });
    });

    it('displays reception hotline and desk info without external navigation when ContactReception is clicked', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Thông tin quầy lễ tân:',
                urgency: 'ROUTINE',
                actions: [
                    {
                        id: 'act-reception',
                        type: 'ContactReception',
                        label: 'Liên hệ lễ tân',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        payload: {}
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <LocationDisplay />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Cho tôi gặp lễ tân' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Liên hệ lễ tân')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Liên hệ lễ tân'));

        await waitFor(() => {
            expect(screen.getByText(/Thông tin Quầy Tiếp Đón & Lễ Tân/i)).toBeInTheDocument();
            expect(screen.getByText(/Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống/i)).toBeInTheDocument();
            // Did not navigate to /contact
            expect(screen.getByTestId('location-display').textContent).toBe('/patient');
        });
    });

    it('renders ReviewBooking summary and allows confirmation from review', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Vui lòng kiểm tra lại thông tin:',
                urgency: 'ROUTINE',
                actions: [
                    {
                        id: 'act-review',
                        type: 'ReviewBooking',
                        label: 'Kiểm tra lại thông tin',
                        style: 'secondary',
                        requiresAuthentication: true,
                        requiresConfirmation: false,
                        payload: {
                            specialtyId: 1,
                            specialtyName: 'Tim Mạch',
                            doctorId: 10,
                            doctorName: 'BS. CKII Nguyễn Văn B',
                            slotId: 105,
                            slotDate: '2026-09-18',
                            startTime: '10:00',
                            endTime: '10:30',
                            reason: 'Tái khám huyết áp'
                        }
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
        fireEvent.change(input, { target: { value: 'Xem lại thông tin đặt' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Kiểm tra lại thông tin')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Kiểm tra lại thông tin'));

        await waitFor(() => {
            expect(screen.getByText(/Thông tin xác nhận lịch hẹn/i)).toBeInTheDocument();
            expect(screen.getByText('Xác nhận đặt lịch')).toBeInTheDocument();
        });
    });

    it('validates isSafeClientRoute rejecting unsafe and allowing valid routes', () => {
        expect(isSafeClientRoute('//evil.com')).toBe(false);
        expect(isSafeClientRoute('https://evil.com')).toBe(false);
        expect(isSafeClientRoute('/patient/invoices.evil')).toBe(false);
        expect(isSafeClientRoute('/malicious/path')).toBe(false);
        expect(isSafeClientRoute('')).toBe(false);
        expect(isSafeClientRoute(undefined)).toBe(false);

        expect(isSafeClientRoute('/patient/invoices')).toBe(true);
        expect(isSafeClientRoute('/patient/appointments?appointmentId=10&action=reschedule')).toBe(true);
        expect(isSafeClientRoute('/patient/book?specialtyId=2')).toBe(true);
        expect(isSafeClientRoute('/doctors?specialtyId=1')).toBe(true);
        expect(isSafeClientRoute('tel:115')).toBe(true);
    });

    it('displays unconfigured message when ContactReception has no phoneNumber', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Thông tin quầy lễ tân:',
                urgency: 'ROUTINE',
                actions: [
                    {
                        id: 'act-reception-unconfigured',
                        type: 'ContactReception',
                        label: 'Liên hệ lễ tân',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        payload: {}
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
        fireEvent.change(input, { target: { value: 'Liên hệ lễ tân giúp tôi' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Liên hệ lễ tân')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Liên hệ lễ tân'));

        await waitFor(() => {
            expect(screen.getByText(/Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống/i)).toBeInTheDocument();
        });
    });

    it('rejects incomplete ReviewBooking and does not generate ConfirmBooking', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Thông tin chưa hoàn tất:',
                urgency: 'ROUTINE',
                actions: [
                    {
                        id: 'act-review-incomplete',
                        type: 'ReviewBooking',
                        label: 'Xem lại thông tin',
                        style: 'secondary',
                        requiresAuthentication: true,
                        requiresConfirmation: false,
                        payload: {
                            specialtyId: 1,
                            // doctorId and slotId missing
                            reason: ''
                        }
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
        fireEvent.change(input, { target: { value: 'Xem lại' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Xem lại thông tin')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xem lại thông tin'));

        await waitFor(() => {
            expect(screen.getByText(/Thông tin đặt lịch chưa đầy đủ/i)).toBeInTheDocument();
            expect(screen.queryByText('Xác nhận đặt lịch')).not.toBeInTheDocument();
        });
    });

    it('handles Escape key to close chat widget', async () => {
        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        expect(screen.getByRole('dialog', { name: /ClinicCare AI/i })).toBeInTheDocument();

        fireEvent.keyDown(window, { key: 'Escape' });

        await waitFor(() => {
            expect(screen.queryByRole('dialog', { name: /ClinicCare AI/i })).not.toBeInTheDocument();
            expect(screen.getByLabelText('Mở Trợ lý ClinicCare AI')).toBeInTheDocument();
        });
    });

    it('rejects ConfirmBooking when reason is under 10 characters and does not call appointment api', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Vui lòng xác nhận lịch:',
                urgency: 'ROUTINE',
                actions: [
                    {
                        id: 'act-confirm-short-reason',
                        type: 'ConfirmBooking',
                        label: 'Xác nhận đặt lịch',
                        style: 'primary',
                        requiresAuthentication: true,
                        requiresConfirmation: true,
                        payload: {
                            specialtyId: 1,
                            doctorId: 1,
                            slotId: 101,
                            slotDate: '2026-09-15',
                            startTime: '08:00',
                            endTime: '08:30',
                            reason: 'Đau đầu' // Only 7 chars (< 10)
                        }
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
        fireEvent.change(input, { target: { value: 'Xác nhận giúp tôi' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Xác nhận đặt lịch')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xác nhận đặt lịch'));

        await waitFor(() => {
            expect(screen.getByText(/Lý do khám phải từ 10 đến 500 ký tự/i)).toBeInTheDocument();
        });

        // Must NOT have called /appointments
        expect(axiosClient.post).not.toHaveBeenCalledWith(
            '/appointments',
            expect.anything()
        );
    });

    it('TC13: SafeMarkdown renders markdown tokens and neutralizes dangerous html / javascript links', () => {
        const markdown = `
**Chữ đậm** và *chữ nghiêng*
Đoạn mã: \`const x = 10;\`
- Mục danh sách 1
- Mục danh sách 2
[Liên kết an toàn](/patient/book)
[Liên kết nguy hiểm](javascript:alert('xss'))
<script>alert('hack')</script>
<img src="x" onerror="alert('xss')" />
        `.trim();

        const { container } = render(<SafeMarkdown content={markdown} />);

        // 1. Bold
        const bold = container.querySelector('strong');
        expect(bold).toBeInTheDocument();
        expect(bold?.textContent).toBe('Chữ đậm');

        // 2. Italic
        const italic = container.querySelector('em');
        expect(italic).toBeInTheDocument();
        expect(italic?.textContent).toBe('chữ nghiêng');

        // 3. Code
        const code = container.querySelector('code');
        expect(code).toBeInTheDocument();
        expect(code?.textContent).toBe('const x = 10;');

        // 4. List
        const listItems = container.querySelectorAll('li');
        expect(listItems.length).toBe(2);
        expect(listItems[0].textContent).toBe('Mục danh sách 1');
        expect(listItems[1].textContent).toBe('Mục danh sách 2');

        // 5. Safe Link
        const safeLink = container.querySelector('a[href="/patient/book"]');
        expect(safeLink).toBeInTheDocument();
        expect(safeLink?.textContent).toBe('Liên kết an toàn');

        // 6. XSS Link neutralization
        const dangerousLink = container.querySelector('a[href*="javascript"]');
        expect(dangerousLink).not.toBeInTheDocument();
        expect(container.textContent).toContain('Liên kết nguy hiểm');

        // 7. Script / Img tag neutralization (never executed or injected as live DOM element)
        expect(container.querySelector('script')).not.toBeInTheDocument();
        expect(container.querySelector('img')).not.toBeInTheDocument();
    });

    it('TC14: MedicalChatWidget displays dynamic status pill matching AI assistant status', async () => {
        const StatusController = () => {
            const { setAiAssistantStatus } = useChatContext();
            return (
                <div>
                    <button onClick={() => setAiAssistantStatus("Online")}>Set Online</button>
                    <button onClick={() => setAiAssistantStatus("Degraded")}>Set Degraded</button>
                    <button onClick={() => setAiAssistantStatus("Offline")}>Set Offline</button>
                </div>
            );
        };

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <StatusController />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));

        // Default status is Unchecked ("Chưa kiểm tra AI") before Gemini is verified
        expect(screen.getByText('Chưa kiểm tra AI')).toBeInTheDocument();

        // Switch to Online ("Trực tuyến")
        fireEvent.click(screen.getByText('Set Online'));
        expect(screen.getByText('Trực tuyến')).toBeInTheDocument();

        // Switch to Degraded ("Chế độ rút gọn")
        fireEvent.click(screen.getByText('Set Degraded'));
        expect(screen.getByText('Chế độ rút gọn')).toBeInTheDocument();

        // Switch to Offline ("Ngoại tuyến")
        fireEvent.click(screen.getByText('Set Offline'));
        expect(screen.getByText('Ngoại tuyến')).toBeInTheDocument();
    });

    it('TC16: Clearing conversation clears messages and active draft from state and storage', async () => {
        const DraftSetter = () => {
            const { setActiveDraft, activeDraft } = useChatContext();
            return (
                <div>
                    <button onClick={() => setActiveDraft({ specialtyId: 1, specialtyName: 'Tim Mạch', version: 1, isComplete: false })}>
                        Set Draft
                    </button>
                    <div data-testid="draft-info">{activeDraft?.specialtyName ?? 'NoDraft'}</div>
                </div>
            );
        };

        render(
            <MemoryRouter initialEntries={['/patient']}>
                <ChatProvider>
                    <MedicalChatWidget />
                    <DraftSetter />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        fireEvent.click(screen.getByText('Set Draft'));

        expect(screen.getByTestId('draft-info')).toHaveTextContent('Tim Mạch');

        // Click Clear Chat button in header
        fireEvent.click(screen.getByLabelText('Làm mới cuộc trò chuyện'));

        await waitFor(() => {
            expect(screen.getByTestId('draft-info')).toHaveTextContent('NoDraft');
        });
    });

    it('TC10: Stale button version check warns user when action draftVersion is older than active draft version', async () => {
        const DraftVersionController = () => {
            const { setActiveDraft } = useChatContext();
            return (
                <button onClick={() => setActiveDraft({ specialtyId: 1, specialtyName: 'Nội tiết', version: 2, isComplete: false })}>
                    Update to v2
                </button>
            );
        };

        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Chọn bác sĩ:',
                urgency: 'ROUTINE',
                bookingDraft: { specialtyId: 1, version: 1 },
                actions: [
                    {
                        id: 'act-doc-1',
                        type: 'SelectDoctor',
                        label: 'Chọn BS Nguyễn Văn A',
                        style: 'secondary',
                        requiresAuthentication: false,
                        requiresConfirmation: false,
                        draftVersion: 1,
                        payload: { specialtyId: 1, doctorId: 10, doctorName: 'Nguyễn Văn A', draftVersion: 1 }
                    }
                ]
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
        fireEvent.change(input, { target: { value: 'Khám nội tiết' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Chọn BS Nguyễn Văn A')).toBeInTheDocument();
        });

        // Now active draft updates to v2
        fireEvent.click(screen.getByText('Update to v2'));

        // Click the v1 button
        fireEvent.click(screen.getByText('Chọn BS Nguyễn Văn A'));

        await waitFor(() => {
            expect(screen.getByText(/Thao tác này thuộc phiên bản thảo lịch cũ \(v1\)/i)).toBeInTheDocument();
        });
    });

    it('TC8_TC9: BookAppointment validates required reason with character limit and sends Idempotency-Key', async () => {
        const specialties = [
            { id: 1, specialtyCode: 'TM', specialtyName: 'Tim mạch', description: 'Tim', aiEnabled: true }
        ];
        const doctors = [
            { id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'BS', specialtyId: 1, specialtyName: 'Tim mạch' }
        ];
        const slots = [
            { id: 1001, slotId: 1001, doctorId: 101, slotDate: '2026-09-23', startTime: '09:00:00', endTime: '09:30:00', isAvailable: true }
        ];

        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url === '/specialties') return Promise.resolve({ success: true, message: '', data: specialties });
            if (url === '/specialties/1/doctors') return Promise.resolve({ success: true, message: '', data: doctors });
            if (url.includes('/doctors/101/available-slots')) return Promise.resolve({ success: true, message: '', data: slots });
            return Promise.resolve({ success: true, message: '', data: [] });
        });

        render(
            <MemoryRouter initialEntries={['/patient/book?specialtyId=1&doctorId=101&date=2026-09-23']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        // Step 1 is pre-selected with specialtyId=1, wait for enabled and click Next to Doctor selection
        const toStep2Btn = await screen.findByText(/Tiếp tục: Chọn bác sĩ/i);
        await waitFor(() => expect(toStep2Btn).not.toBeDisabled());
        fireEvent.click(toStep2Btn);

        // Step 2: Doctor "Nguyễn Văn An" is loaded
        await waitFor(() => {
            expect(screen.getAllByText(/Nguyễn Văn An/i).length).toBeGreaterThan(0);
        });
        const toStep3Btn = screen.getByText(/Tiếp tục: Chọn giờ khám/i);
        fireEvent.click(toStep3Btn);

        // Step 3: Slot selection & Reason input
        await waitFor(() => {
            expect(screen.getByText(/09:00/i)).toBeInTheDocument();
        });

        // Select slot
        const slotBtn = await screen.findByRole('button', { name: /09:00/i });
        fireEvent.click(slotBtn);

        // Check reason field & character counter
        expect(screen.getByText(/0\/500 ký tự/i)).toBeInTheDocument();

        // Reason input too short (< 10 chars)
        const reasonTextarea = screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.change(reasonTextarea, { target: { value: 'Đau tức' } });

        // Next to Step 4 button must be disabled
        const nextToStep4Btn = screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i });
        expect(nextToStep4Btn).toBeDisabled();
        expect(screen.getByText('Lý do khám phải có tối thiểu 10 ký tự.')).toBeInTheDocument();

        // Enter valid reason >= 10 chars
        fireEvent.change(reasonTextarea, { target: { value: 'Đau tức ngực trái khi gắng sức' } });
        await waitFor(() => expect(nextToStep4Btn).not.toBeDisabled());

        // Click next to Step 4
        fireEvent.click(nextToStep4Btn);

        // In Step 4: Click "Xác nhận & Đặt lịch"
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Đặt lịch thành công',
            data: {
                id: 555,
                appointmentCode: 'APT-20260923-555',
                doctorName: 'BS Nguyễn Văn An',
                specialtyName: 'Tim mạch',
                appointmentDate: '2026-09-23',
                startTime: '09:00',
                endTime: '09:30'
            }
        });

        const confirmBtn = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });
        fireEvent.click(confirmBtn);

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenCalledWith(
                '/appointments',
                expect.objectContaining({
                    doctorId: 101,
                    specialtyId: 1,
                    appointmentSlotId: 1001,
                    reason: 'Đau tức ngực trái khi gắng sức'
                }),
                expect.objectContaining({
                    headers: expect.objectContaining({
                        'Idempotency-Key': expect.any(String)
                    })
                })
            );
        });
    });

    it('Phase0_FormEdit_InvalidatesOldChatConfirmation_AndIncrementsDraftVersion', async () => {
        const specialties = [
            { id: 1, specialtyCode: 'TM', specialtyName: 'Tim mạch', description: 'Tim', aiEnabled: true }
        ];
        const doctors = [
            { id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'BS', specialtyId: 1, specialtyName: 'Tim mạch' }
        ];
        const slots = [
            { id: 1001, slotId: 1001, doctorId: 101, slotDate: '2026-09-23', startTime: '09:00:00', endTime: '09:30:00', isAvailable: true }
        ];

        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url === '/specialties') return Promise.resolve({ success: true, message: '', data: specialties });
            if (url === '/specialties/1/doctors') return Promise.resolve({ success: true, message: '', data: doctors });
            if (url.includes('/doctors/101/available-slots')) return Promise.resolve({ success: true, message: '', data: slots });
            return Promise.resolve({ success: true, message: '', data: [] });
        });

        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Vui lòng xác nhận lịch khám:',
                urgency: 'ROUTINE',
                providerStatus: 'Healthy',
                assistantStatus: 'Online',
                bookingDraft: {
                    specialtyId: 1,
                    specialtyName: 'Tim mạch',
                    doctorId: 101,
                    doctorName: 'BS Nguyễn Văn An',
                    slotId: 1001,
                    slotDate: '2026-09-23',
                    startTime: '09:00',
                    endTime: '09:30',
                    reason: 'Đau tức ngực trái kéo dài 3 ngày',
                    isComplete: true,
                    version: 1,
                    confirmationId: 'conf_v1_1001'
                },
                actions: [
                    {
                        id: 'act-confirm-v1',
                        type: 'ConfirmBooking',
                        label: 'Xác nhận đặt lịch v1',
                        style: 'primary',
                        requiresAuthentication: true,
                        requiresConfirmation: true,
                        draftVersion: 1,
                        payload: {
                            confirmationId: 'conf_v1_1001',
                            specialtyId: 1,
                            specialtyName: 'Tim mạch',
                            doctorId: 101,
                            doctorName: 'BS Nguyễn Văn An',
                            slotId: 1001,
                            slotDate: '2026-09-23',
                            startTime: '09:00',
                            endTime: '09:30',
                            reason: 'Đau tức ngực trái kéo dài 3 ngày',
                            draftVersion: 1
                        }
                    }
                ]
            }
        });

        render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                        <MedicalChatWidget />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const chatInput = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(chatInput, { target: { value: 'Đặt lịch tim mạch' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Xác nhận đặt lịch v1')).toBeInTheDocument();
        });

        // Edit reason on BookAppointment form -> increments draft version to 2 and clears confirmationId
        const reasonTextarea = await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.change(reasonTextarea, { target: { value: 'Đau tức ngực trái kèm khó thở về đêm' } });

        // Clicking old ConfirmBooking v1 in chat must be rejected and NOT call /appointments
        fireEvent.click(screen.getByText('Xác nhận đặt lịch v1'));

        await waitFor(() => {
            expect(screen.getByText(/thuộc phiên bản cũ \(v1\)/i)).toBeInTheDocument();
        });
        expect(axiosClient.post).toHaveBeenCalledTimes(1); // Only the initial /ai/chat call
    });

    it('Phase0_CancelDraft_BlocksStaleActionButtons_AndPreventsResurrection', async () => {
        vi.mocked(axiosClient.post)
            .mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    message: 'Chọn bác sĩ:',
                    urgency: 'ROUTINE',
                    providerStatus: 'Healthy',
                    assistantStatus: 'Online',
                    bookingDraft: { specialtyId: 1, specialtyName: 'Tim mạch', version: 1, isComplete: false },
                    actions: [
                        {
                            id: 'act-doc-stale',
                            type: 'SelectDoctor',
                            label: 'Chọn BS Trần Văn B',
                            style: 'secondary',
                            requiresAuthentication: false,
                            requiresConfirmation: false,
                            draftVersion: 1,
                            payload: { specialtyId: 1, doctorId: 102, doctorName: 'Trần Văn B', draftVersion: 1 }
                        }
                    ]
                }
            })
            .mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    message: 'Đã hủy bản nháp đặt lịch hiện tại.',
                    urgency: 'ROUTINE',
                    dialogueOutcome: 'DraftCancelled',
                    providerStatus: 'NotCalled',
                    assistantStatus: 'Online',
                    bookingDraft: null,
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
        const chatInput = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(chatInput, { target: { value: 'Đặt lịch khám tim' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText('Chọn BS Trần Văn B')).toBeInTheDocument();
        });

        // Cancel draft
        fireEvent.change(chatInput, { target: { value: 'Hủy đặt lịch' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText(/Đã hủy bản nháp đặt lịch hiện tại/i)).toBeInTheDocument();
        });

        // Clicking old SelectDoctor button after cancellation must be blocked
        fireEvent.click(screen.getByText('Chọn BS Trần Văn B'));
        await waitFor(() => {
            expect(screen.getByText(/Bản nháp đặt lịch trước đó đã bị hủy/i)).toBeInTheDocument();
        });
        expect(axiosClient.post).toHaveBeenCalledTimes(2);
    });

    it('Phase0_ProviderStatus_503Degraded_NotPromotedByNotCalled_UntilHealthy', async () => {
        vi.mocked(axiosClient.post)
            .mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    message: 'Đang dùng chế độ dự phòng.',
                    urgency: 'ROUTINE',
                    providerStatus: 'Unavailable',
                    assistantStatus: 'Degraded',
                    actions: []
                }
            })
            .mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    message: 'Giá khám là 150.000đ.',
                    urgency: 'ROUTINE',
                    providerStatus: 'NotCalled',
                    assistantStatus: 'Online',
                    actions: []
                }
            })
            .mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    message: 'Gemini đã kết nối lại.',
                    urgency: 'ROUTINE',
                    providerStatus: 'Healthy',
                    assistantStatus: 'Online',
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
        expect(screen.getByText('Chưa kiểm tra AI')).toBeInTheDocument();

        const chatInput = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');

        // 1. 503 Unavailable -> Degraded ("Chế độ rút gọn")
        fireEvent.change(chatInput, { target: { value: 'Tôi bị đau đầu' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));
        await waitFor(() => {
            expect(screen.getByText('Chế độ rút gọn')).toBeInTheDocument();
        });

        // 2. Local turn with providerStatus: NotCalled -> must STAY Degraded ("Chế độ rút gọn")
        fireEvent.change(chatInput, { target: { value: 'Giá khám bao nhiêu' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));
        await waitFor(() => {
            expect(screen.getByText(/150\.000đ/i)).toBeInTheDocument();
        });
        expect(screen.getByText('Chế độ rút gọn')).toBeInTheDocument();

        // 3. Real Gemini success with providerStatus: Healthy -> Online ("Trực tuyến")
        fireEvent.change(chatInput, { target: { value: 'Tư vấn thêm giúp tôi' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));
        await waitFor(() => {
            expect(screen.getByText('Trực tuyến')).toBeInTheDocument();
        });
    });
});

