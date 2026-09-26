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

    it('renders grounded tool data instead of a generic tool-success message', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                message: 'Bảng giá hiện hành từ ClinicCare.',
                urgency: 'ROUTINE',
                sessionId: 'sess_grounded',
                toolResults: [{
                    status: 'completed',
                    resultType: 'pricing_catalog',
                    displayText: 'Bảng giá dưới đây được lấy từ dữ liệu hiện hành của ClinicCare.',
                    data: {
                        consultation: [{ specialtyId: 1, specialty: 'Tim mạch', consultationFee: 250000, currency: 'VND' }],
                        diagnostics: [{ serviceId: 2, name: 'Siêu âm', price: 180000, currency: 'VND' }]
                    }
                }]
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

        expect(await screen.findByText(/Tim mạch: 250\.000 VND/i)).toBeInTheDocument();
        expect(screen.getByText(/Siêu âm: 180\.000 VND/i)).toBeInTheDocument();
        expect(screen.queryByText('Đã kiểm tra dữ liệu hệ thống')).not.toBeInTheDocument();
    });

    it('confirms pending copilot actions through the dedicated endpoint with session binding', async () => {
        vi.mocked(axiosClient.post)
            .mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    message: 'Bạn có một thao tác đang chờ xác nhận.',
                    urgency: 'ROUTINE',
                    sessionId: 'sess_confirm',
                    toolResults: [{
                        status: 'pending_confirmation',
                        actionId: '11111111-1111-1111-1111-111111111111',
                        resultType: 'pending_action',
                        data: { appointmentCode: 'APPT-42', concurrencyToken: 'AA==' }
                    }]
                }
            })
            .mockResolvedValueOnce({
                status: 'completed',
                resultType: 'change_request',
                displayText: 'Yêu cầu hủy lịch đã được tạo.',
                data: { changeRequestId: 42 }
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
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận thực hiện' }));

        await waitFor(() => expect(axiosClient.post).toHaveBeenCalledTimes(2));
        expect(axiosClient.post.mock.calls[1][0]).toBe('/ai/tool-actions/11111111-1111-1111-1111-111111111111/confirm');
        expect(axiosClient.post.mock.calls[1][1]).toEqual({ sessionId: 'sess_confirm', concurrencyToken: 'AA==' });
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

    it('rejects ReviewBooking without a persisted confirmation and does not invent an ID', async () => {
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
            expect(screen.getByText(/Chưa có mã xác nhận đặt lịch hợp lệ từ hệ thống/i)).toBeInTheDocument();
            expect(screen.queryByText('Xác nhận đặt lịch')).not.toBeInTheDocument();
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

    it('P1_StaleResponse_A_SucceedsAfterDraftModifiedToB_PreservesDraftB_AndDoesNotOverwriteUI', async () => {
        const specialties = [{ id: 1, specialtyCode: 'SP01', specialtyName: 'Tim mạch', description: 'Khoa Tim mạch' }];
        const doctors = [{ id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'BS', specialtyId: 1, specialtyName: 'Tim mạch' }];
        const slots = [
            { id: 1001, slotId: 1001, doctorId: 101, slotDate: '2026-09-23', startTime: '09:00:00', endTime: '09:30:00', isAvailable: true },
            { id: 1002, slotId: 1002, doctorId: 101, slotDate: '2026-09-23', startTime: '10:00:00', endTime: '10:30:00', isAvailable: true }
        ];

        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url === '/specialties') return Promise.resolve({ success: true, message: '', data: specialties });
            if (url === '/specialties/1/doctors') return Promise.resolve({ success: true, message: '', data: doctors });
            if (url.includes('/doctors/101/available-slots')) return Promise.resolve({ success: true, message: '', data: slots });
            return Promise.resolve({ success: true, message: '', data: [] });
        });

        let resolveRequestA!: (val: unknown) => void;
        const deferredA = new Promise((resolve) => {
            resolveRequestA = resolve;
        });

        let chatTurnA = 0;
        vi.mocked(axiosClient.post).mockImplementation((url: string) => {
            if (url === '/appointments') {
                return deferredA as Promise<unknown>;
            }
            chatTurnA += 1;
            if (chatTurnA === 1) {
                return Promise.resolve({
                    success: true,
                    message: '',
                    data: {
                        message: 'Đã chuẩn bị bản nháp A',
                        urgency: 'ROUTINE',
                        providerStatus: 'Healthy',
                        assistantStatus: 'Online',
                        bookingDraft: {
                            draftId: 'draft-a',
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
                            confirmationId: 'conf_a_1'
                        },
                        actions: []
                    }
                });
            }
            return Promise.resolve({
                success: true,
                message: '',
                data: {
                    message: 'Đã cập nhật sang bản nháp B',
                    urgency: 'ROUTINE',
                    providerStatus: 'Healthy',
                    assistantStatus: 'Online',
                    bookingDraft: {
                        draftId: 'draft-a',
                        specialtyId: 1,
                        specialtyName: 'Tim mạch',
                        doctorId: 101,
                        doctorName: 'BS Nguyễn Văn An',
                        slotId: 1002,
                        slotDate: '2026-09-23',
                        startTime: '10:00',
                        endTime: '10:30',
                        reason: 'Đau tức ngực trái kèm khó thở về đêm (Draft B)',
                        isComplete: true,
                        version: 2,
                        confirmationId: 'conf_b_2'
                    },
                    actions: []
                }
            });
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

        // Seed Draft A via chat
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const chatInput = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(chatInput, { target: { value: 'Đặt lịch khám tim mạch' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const reasonTextarea = await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        await waitFor(() => {
            expect((reasonTextarea as HTMLTextAreaElement).value).toBe('Đau tức ngực trái kéo dài 3 ngày');
        });

        // Navigate to step 4 and submit Request A
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        const confirmBtn = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });
        fireEvent.click(confirmBtn);

        // While Request A is in-flight, user modifies draft into Draft B via chat
        fireEvent.change(chatInput, { target: { value: 'Đổi sang giờ 10:00 và lý do mới' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));
        await waitFor(() => {
            expect(screen.getByText('Đã cập nhật sang bản nháp B')).toBeInTheDocument();
        });

        // Now resolve stale Request A
        resolveRequestA({
            success: true,
            message: '',
            data: {
                id: 901,
                appointmentCode: 'APT-901',
                doctorName: 'BS Nguyễn Văn An',
                specialtyName: 'Tim mạch',
                startTime: '09:00:00',
                endTime: '09:30:00',
                reason: 'Đau tức ngực trái kéo dài 3 ngày'
            }
        });

        await new Promise(r => setTimeout(r, 30));

        // Draft B must remain intact in sessionStorage and UI, NOT wiped or replaced by Request A's success screen
        expect(screen.queryByText('APT-901')).not.toBeInTheDocument();
        const savedDraftBRaw = sessionStorage.getItem('cliniccare_booking_draft_pat-1');
        expect(savedDraftBRaw).not.toBeNull();
        const savedDraftB = JSON.parse(savedDraftBRaw!);
        expect(savedDraftB.slotId).toBe(1002);
        expect(savedDraftB.version).toBe(2);
        expect(savedDraftB.reason).toBe('Đau tức ngực trái kèm khó thở về đêm (Draft B)');
    });

    it('P1_StaleResponse_A_ReturnsSlotAlreadyBooked_AfterCancelAndNewDraftB_PreservesDraftBSlotAndVersion', async () => {
        const specialties = [{ id: 1, specialtyCode: 'SP01', specialtyName: 'Tim mạch', description: 'Khoa Tim mạch' }];
        const doctors = [{ id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'BS', specialtyId: 1, specialtyName: 'Tim mạch' }];
        const slots = [
            { id: 1001, slotId: 1001, doctorId: 101, slotDate: '2026-09-23', startTime: '09:00:00', endTime: '09:30:00', isAvailable: true },
            { id: 1002, slotId: 1002, doctorId: 101, slotDate: '2026-09-23', startTime: '10:00:00', endTime: '10:30:00', isAvailable: true }
        ];

        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url === '/specialties') return Promise.resolve({ success: true, message: '', data: specialties });
            if (url === '/specialties/1/doctors') return Promise.resolve({ success: true, message: '', data: doctors });
            if (url.includes('/doctors/101/available-slots')) return Promise.resolve({ success: true, message: '', data: slots });
            return Promise.resolve({ success: true, message: '', data: [] });
        });

        let rejectRequestA!: (err: unknown) => void;
        const deferredA = new Promise((_, reject) => {
            rejectRequestA = reject;
        });

        let chatTurn = 0;
        vi.mocked(axiosClient.post).mockImplementation((url: string) => {
            if (url === '/appointments') {
                return deferredA as Promise<unknown>;
            }
            chatTurn += 1;
            if (chatTurn === 1) {
                return Promise.resolve({
                    success: true,
                    message: '',
                    data: {
                        message: 'Bản nháp A',
                        urgency: 'ROUTINE',
                        providerStatus: 'Healthy',
                        assistantStatus: 'Online',
                        bookingDraft: {
                            draftId: 'draft-a',
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
                            confirmationId: 'conf_a_1'
                        },
                        actions: []
                    }
                });
            }
            if (chatTurn === 2) {
                return Promise.resolve({
                    success: true,
                    message: '',
                    data: {
                        message: 'Đã hủy bản nháp A',
                        urgency: 'ROUTINE',
                        providerStatus: 'NotCalled',
                        assistantStatus: 'Online',
                        dialogueOutcome: 'DraftCancelled',
                        bookingDraft: undefined,
                        actions: []
                    }
                });
            }
            return Promise.resolve({
                success: true,
                message: '',
                data: {
                    message: 'Bản nháp B mới',
                    urgency: 'ROUTINE',
                    providerStatus: 'Healthy',
                    assistantStatus: 'Online',
                    bookingDraft: {
                        draftId: 'draft-b',
                        specialtyId: 1,
                        specialtyName: 'Tim mạch',
                        doctorId: 101,
                        doctorName: 'BS Nguyễn Văn An',
                        slotId: 1002,
                        slotDate: '2026-09-23',
                        startTime: '10:00',
                        endTime: '10:30',
                        reason: 'Khám định kỳ tim mạch tổng quát (Draft B)',
                        isComplete: true,
                        version: 1,
                        confirmationId: 'conf_b_1'
                    },
                    actions: []
                }
            });
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

        // 1. Create Draft A
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const chatInput = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(chatInput, { target: { value: 'Đặt lịch A' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        const confirmBtn = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });
        fireEvent.click(confirmBtn);

        // 2. Cancel Draft A via chat, then create Draft B with slot 1002
        fireEvent.change(chatInput, { target: { value: 'Hủy đặt lịch' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));
        await waitFor(() => {
            expect(screen.getByText('Đã hủy bản nháp A')).toBeInTheDocument();
        });

        fireEvent.change(chatInput, { target: { value: 'Tạo lịch B' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));
        await waitFor(() => {
            expect(screen.getByText('Bản nháp B mới')).toBeInTheDocument();
        });

        // 3. Reject stale Request A with SLOT_ALREADY_BOOKED
        rejectRequestA({
            response: {
                data: {
                    errorCode: 'SLOT_ALREADY_BOOKED',
                    message: 'Khung giờ này vừa được đặt bởi người khác.'
                }
            }
        });

        await new Promise(r => setTimeout(r, 30));

        // Draft B in sessionStorage must still retain slotId: 1002, version: 1, confirmationId: 'conf_b_1'
        const savedDraftRaw = sessionStorage.getItem('cliniccare_booking_draft_pat-1');
        expect(savedDraftRaw).not.toBeNull();
        const savedDraft = JSON.parse(savedDraftRaw!);
        expect(savedDraft.slotId).toBe(1002);
        expect(savedDraft.version).toBe(1);
        expect(savedDraft.confirmationId).toBe('conf_b_1');
    });

    it('P1_Idempotency_TimeoutRetrySameTurn_CancelNewTurn_ModifyPayload_AndRemountReconciliation', async () => {
        const specialties = [{ id: 1, specialtyCode: 'SP01', specialtyName: 'Tim mạch', description: 'Khoa Tim mạch' }];
        const doctors = [{ id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'BS', specialtyId: 1, specialtyName: 'Tim mạch' }];
        const slots = [
            { id: 1001, slotId: 1001, doctorId: 101, slotDate: '2026-09-23', startTime: '09:00:00', endTime: '09:30:00', isAvailable: true },
            { id: 1002, slotId: 1002, doctorId: 101, slotDate: '2026-09-23', startTime: '10:00:00', endTime: '10:30:00', isAvailable: true }
        ];

        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url === '/specialties') return Promise.resolve({ success: true, message: '', data: specialties });
            if (url === '/specialties/1/doctors') return Promise.resolve({ success: true, message: '', data: doctors });
            if (url.includes('/doctors/101/available-slots')) return Promise.resolve({ success: true, message: '', data: slots });
            return Promise.resolve({ success: true, message: '', data: [] });
        });

        const capturedKeys: string[] = [];
        let shouldTimeout = true;

        vi.mocked(axiosClient.post).mockImplementation((url: string, _data?: unknown, config?: unknown) => {
            if (url === '/appointments') {
                const headers = (config as { headers?: Record<string, string> } | undefined)?.headers;
                const key = headers?.['Idempotency-Key'] || '';
                capturedKeys.push(key);
                if (shouldTimeout) {
                    return Promise.reject(new Error('Network timeout'));
                }
                return Promise.resolve({
                    success: true,
                    message: '',
                    data: {
                        id: 999,
                        appointmentCode: 'APT-999',
                        doctorName: 'BS Nguyễn Văn An',
                        specialtyName: 'Tim mạch',
                        startTime: '09:00:00',
                        endTime: '09:30:00',
                        reason: 'Đau tức ngực trái kéo dài 3 ngày'
                    }
                });
            }
            return Promise.resolve({
                success: true,
                message: '',
                data: {
                    message: 'Đã chuẩn bị bản nháp',
                    urgency: 'ROUTINE',
                    providerStatus: 'Healthy',
                    assistantStatus: 'Online',
                    bookingDraft: {
                        draftId: `draft-${capturedKeys.length}`,
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
                        confirmationId: `conf_${capturedKeys.length}`
                    },
                    actions: []
                }
            });
        });

        // Pre-populate active draft in sessionStorage for user 1
        sessionStorage.setItem('cliniccare_booking_draft_pat-1', JSON.stringify({
            draftId: 'draft-turn-1',
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
            confirmationId: 'conf_turn_1'
        }));

        const { unmount } = render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        const confirmBtn = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });

        // (a) First attempt times out -> records key K1
        fireEvent.click(confirmBtn);
        await waitFor(() => {
            expect(capturedKeys.length).toBe(1);
        });
        const key1 = capturedKeys[0];
        expect(key1).toBeTruthy();

        // (f) Simulate remount/reload while outcome of K1 is still uncertain!
        unmount();
        const { unmount: unmount2 } = render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        await waitFor(() => {
            expect(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i })).not.toBeDisabled();
        });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        const confirmBtnAfterRemount = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });

        // Retry after remount with same turn & payload MUST reuse key1!
        fireEvent.click(confirmBtnAfterRemount);
        await waitFor(() => {
            expect(capturedKeys.length).toBe(2);
        });
        expect(capturedKeys[1]).toBe(key1);

        // (d) Now user edits reason -> must generate a NEW key (key2 !== key1)
        fireEvent.click(screen.getByRole('button', { name: /Quay lại/i }));
        const reasonInput = await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.change(reasonInput, { target: { value: 'Đau tức ngực trái kéo dài 5 ngày kèm mệt' } });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        fireEvent.click(await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i }));

        await waitFor(() => {
            expect(capturedKeys.length).toBe(3);
        });
        const key2 = capturedKeys[2];
        expect(key2).not.toBe(key1);

        // (c) Cancel draft -> start a NEW draft with the EXACT same payload as key2 -> MUST generate a brand new key (key3 !== key2)!
        shouldTimeout = false;
        unmount2();
        sessionStorage.removeItem('cliniccare_booking_draft_pat-1');
        sessionStorage.removeItem('cliniccare_pending_booking_attempt_pat-1');
        sessionStorage.setItem('cliniccare_booking_draft_pat-1', JSON.stringify({
            draftId: 'draft-turn-2-fresh',
            specialtyId: 1,
            specialtyName: 'Tim mạch',
            doctorId: 101,
            doctorName: 'BS Nguyễn Văn An',
            slotId: 1001,
            slotDate: '2026-09-23',
            startTime: '09:00',
            endTime: '09:30',
            reason: 'Đau tức ngực trái kéo dài 5 ngày kèm mệt',
            isComplete: true,
            version: 1,
            confirmationId: 'conf_turn_2'
        }));

        render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        await waitFor(() => {
            expect(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i })).not.toBeDisabled();
        });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        fireEvent.click(await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i }));

        await waitFor(() => {
            expect(capturedKeys.length).toBe(4);
        });
        const key3 = capturedKeys[3];
        expect(key3).toBeTruthy();
        expect(key3).not.toBe(key2);
        expect(key3).not.toBe(key1);

        // (2d) Response in valid context updates success state normally
        await waitFor(() => {
            expect(screen.getByText(/Đặt lịch khám thành công/i)).toBeInTheDocument();
            expect(screen.getAllByText(/APT-999/i).length).toBeGreaterThan(0);
        });
    });

    it('P1_StaleResponse_A_ResolvesAfterUserSwitchOrUnmount_DoesNotLeakState', async () => {
        const specialties = [{ id: 1, specialtyCode: 'SP01', specialtyName: 'Tim mạch', description: 'Khoa Tim mạch' }];
        const doctors = [{ id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'BS', specialtyId: 1, specialtyName: 'Tim mạch' }];
        const slots = [
            { id: 1001, slotId: 1001, doctorId: 101, slotDate: '2026-09-23', startTime: '09:00:00', endTime: '09:30:00', isAvailable: true }
        ];

        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url === '/specialties') return Promise.resolve({ success: true, message: '', data: specialties });
            if (url === '/specialties/1/doctors') return Promise.resolve({ success: true, message: '', data: doctors });
            if (url.includes('/doctors/101/available-slots')) return Promise.resolve({ success: true, message: '', data: slots });
            return Promise.resolve({ success: true, message: '', data: [] });
        });

        let resolveUser1Post!: (val: unknown) => void;
        const deferredUser1 = new Promise((resolve) => {
            resolveUser1Post = resolve;
        });

        vi.mocked(axiosClient.post).mockImplementation((url: string) => {
            if (url === '/appointments') {
                return deferredUser1 as Promise<unknown>;
            }
            return Promise.resolve({ success: true, message: '', data: {} });
        });

        sessionStorage.setItem('cliniccare_booking_draft_pat-1', JSON.stringify({
            draftId: 'draft-user-1',
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
            confirmationId: 'conf_u1'
        }));

        const { rerender, unmount } = render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        fireEvent.click(await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i }));

        // Switch user to pat-2 while Request A is in-flight
        mockUser = { id: 'pat-2', fullName: 'Patient Two', role: 'Patient' };
        rerender(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        resolveUser1Post({
            success: true,
            message: '',
            data: {
                id: 777,
                appointmentCode: 'APT-LEAK-777',
                doctorName: 'BS Nguyễn Văn An',
                specialtyName: 'Tim mạch',
                startTime: '09:00:00',
                endTime: '09:30:00',
                reason: 'Đau tức ngực trái kéo dài 3 ngày'
            }
        });

        await new Promise(r => setTimeout(r, 30));

        // Must NOT leak User 1's appointment success onto User 2's view
        expect(screen.queryByText(/APT-LEAK-777/i)).not.toBeInTheDocument();
        unmount();
    });

    it('catch block: context change before error -> does NOT clear other turn attempt state', async () => {
        const testStorageKey = 'cliniccare_pending_booking_attempt_pat-1';

        let rejectA!: (err: unknown) => void;
        const deferredA = new Promise((_, reject) => {
            rejectA = reject;
        });

        vi.mocked(axiosClient.post).mockImplementation((url: string) => {
            if (url === '/appointments') {
                return deferredA as Promise<unknown>;
            }
            return Promise.resolve({ success: true, message: '', data: {} });
        });

        // Populate Draft A
        sessionStorage.setItem('cliniccare_booking_draft_pat-1', JSON.stringify({
            draftId: 'draft-A', // Different draft
            specialtyId: 1,
            specialtyName: 'Tim mạch',
            doctorId: 101,
            doctorName: 'BS Nguyễn Văn An',
            slotId: 1001,
            slotDate: '2026-09-23',
            startTime: '09:00',
            endTime: '09:30',
            reason: 'Lý do khám',
            isComplete: true,
            version: 1
        }));

        const { unmount } = render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        // Click to start request A
        await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        fireEvent.click(await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i }));

        await new Promise(r => setTimeout(r, 0));
        unmount();
        
        // 1. Simulate Draft B is the current one in sessionStorage (context changed)
        sessionStorage.setItem(testStorageKey, JSON.stringify({
            accountKey: 'pat-1',
            turnIdentity: 'pat-1_draft-B',
            draftId: 'draft-B',
            payloadFingerprint: 'std_1_101_2026-09-23_1001_Lý do khám',
            key: 'key-B',
            status: 'uncertain'
        }));

        // Now change the active draft to B to simulate context change
        sessionStorage.setItem('cliniccare_booking_draft_pat-1', JSON.stringify({
            draftId: 'draft-B', // Switched!
            specialtyId: 1,
            specialtyName: 'Tim mạch',
            doctorId: 101,
            doctorName: 'BS Nguyễn Văn An',
            slotId: 1001,
            slotDate: '2026-09-23',
            startTime: '09:00',
            endTime: '09:30',
            reason: 'Lý do khám',
            isComplete: true,
            version: 1
        }));
        
        // Reject A with 409 after context switched to B
        rejectA({
            errorCode: 'SLOT_ALREADY_BOOKED',
            response: {
                status: 409,
                data: { errorCode: 'SLOT_ALREADY_BOOKED' }
            }
        });

        await new Promise(r => setTimeout(r, 50));

        // Draft B's attempt should be untouched!
        const finalSaved = sessionStorage.getItem(testStorageKey);
        expect(finalSaved).not.toBeNull();
        if (finalSaved) {
            expect(JSON.parse(finalSaved).key).toBe('key-B');
        }
    });

    it('A1 & A2: widget ConfirmBooking timeout -> unmount/remount ChatProvider & Widget -> retry sends EXACT SAME Idempotency-Key K1 and handles server replay', async () => {
        const capturedKeys: string[] = [];
        const capturedPayloads: unknown[] = [];

        vi.mocked(axiosClient.post).mockImplementation(async (url, data, config) => {
            if (url === '/ai/chat') {
                return {
                    success: true,
                    message: '',
                    data: {
                        message: 'Vui lòng xác nhận lịch khám:',
                        urgency: 'ROUTINE',
                        sessionId: 'sess_a1',
                        draftId: 'draft_a1',
                        contextSnapshotId: 'snap_a1',
                        bookingDraft: {
                            draftId: 'draft_a1',
                            specialtyId: 1,
                            specialtyName: 'Tim mạch',
                            doctorId: 101,
                            doctorName: 'BS Nguyễn Văn An',
                            slotId: 1005,
                            slotDate: '2026-09-24',
                            startTime: '10:00',
                            endTime: '10:30',
                            reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                            isComplete: true,
                            version: 1,
                            confirmationId: 'conf_a1_v1'
                        },
                        actions: [
                            {
                                id: 'act-confirm-a1',
                                type: 'ConfirmBooking',
                                label: 'Xác nhận đặt lịch',
                                style: 'primary',
                                requiresAuthentication: true,
                                requiresConfirmation: true,
                                draftVersion: 1,
                                payload: {
                                    confirmationId: 'conf_a1_v1',
                                    contextSnapshotId: 'snap_a1',
                                    sessionId: 'sess_a1',
                                    draftId: 'draft_a1',
                                    specialtyId: 1,
                                    specialtyName: 'Tim mạch',
                                    doctorId: 101,
                                    doctorName: 'BS Nguyễn Văn An',
                                    slotId: 1005,
                                    slotDate: '2026-09-24',
                                    startTime: '10:00',
                                    endTime: '10:30',
                                    reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                                    draftVersion: 1
                                }
                            }
                        ]
                    }
                };
            }
            if (url === '/appointments') {
                const headers = config?.headers as Record<string, unknown> | undefined;
                const key = String(headers?.['Idempotency-Key'] ?? '');
                capturedKeys.push(key);
                capturedPayloads.push(data);
                if (capturedKeys.length === 1) {
                    const timeoutErr = new Error('timeout of 15000ms exceeded') as Error & { code?: string };
                    timeoutErr.code = 'ECONNABORTED';
                    throw timeoutErr;
                }
                return {
                    success: true,
                    message: 'Idempotent replay',
                    data: {
                        id: 9001,
                        appointmentCode: 'APPT-REPLAY-9001',
                        doctorName: 'BS Nguyễn Văn An',
                        specialtyName: 'Tim mạch',
                        slotDate: '2026-09-24',
                        startTime: '10:00',
                        endTime: '10:30',
                        reason: 'Triệu chứng đau ngực kéo dài 3 ngày'
                    }
                };
            }
            return { success: true, message: '', data: {} };
        });

        const { unmount } = render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Tôi muốn đặt lịch khám tim mạch' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const confirmBtn1 = await screen.findByRole('button', { name: 'Xác nhận đặt lịch' });
        fireEvent.click(confirmBtn1);

        await waitFor(() => {
            expect(capturedKeys.length).toBe(1);
        });
        const k1 = capturedKeys[0];
        expect(k1.length).toBeGreaterThan(5);
        expect(capturedPayloads[0]).toMatchObject({
            confirmationId: 'conf_a1_v1',
            contextSnapshotId: 'snap_a1',
            sessionId: 'sess_a1',
            draftId: 'draft_a1',
            draftVersion: 1
        });

        // Wait for timeout error notice to render so attempt status is updated to "uncertain"
        await waitFor(() => {
            expect(screen.getByText(/timeout of 15000ms exceeded/i)).toBeInTheDocument();
        });

        // Unmount BOTH Widget and ChatProvider
        unmount();

        // Remount ChatProvider and Widget in the same sessionStorage session
        render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const confirmBtnAfterRemount = await screen.findByRole('button', { name: 'Xác nhận đặt lịch' });
        fireEvent.click(confirmBtnAfterRemount);

        await waitFor(() => {
            expect(capturedKeys.length).toBe(2);
        });

        const k2 = capturedKeys[1];
        expect(k2).toBe(k1);
        expect(capturedPayloads[1]).toEqual(capturedPayloads[0]);

        // A2: Verify replay success notice is displayed once and attempt storage is cleaned up
        await waitFor(() => {
            expect(screen.getByText(/APPT-REPLAY-9001/)).toBeInTheDocument();
        });
        expect(screen.getAllByText(/APPT-REPLAY-9001/)).toHaveLength(1);
        expect(sessionStorage.getItem('cliniccare_pending_booking_attempt_pat-1')).toBeNull();
    });

    it('A3 & A4: cancelling draft via UI/API or changing slot/reason invalidates old attempt and generates a NEW Idempotency-Key', async () => {
        const capturedKeys: string[] = [];
        let chatCallIndex = 0;

        vi.mocked(axiosClient.post).mockImplementation(async (url, _data, config) => {
            if (url === '/ai/chat') {
                chatCallIndex += 1;
                if (chatCallIndex === 1) {
                    return {
                        success: true,
                        message: '',
                        data: {
                            message: 'Bản nháp 1:',
                            urgency: 'ROUTINE',
                            sessionId: 'sess_a3',
                            draftId: 'draft_a3_first',
                            bookingDraft: {
                                draftId: 'draft_a3_first',
                                specialtyId: 1,
                                doctorId: 101,
                                slotId: 1005,
                                slotDate: '2026-09-24',
                                startTime: '10:00',
                                endTime: '10:30',
                                reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                                isComplete: true,
                                version: 1,
                                confirmationId: 'conf_a3_1'
                            },
                            actions: [
                                {
                                    id: 'act-confirm-a3-1',
                                    type: 'ConfirmBooking',
                                    label: 'Xác nhận đặt lịch',
                                    style: 'primary',
                                    requiresAuthentication: true,
                                    requiresConfirmation: true,
                                    draftVersion: 1,
                                    payload: {
                                        confirmationId: 'conf_a3_1',
                                        specialtyId: 1,
                                        doctorId: 101,
                                        slotId: 1005,
                                        slotDate: '2026-09-24',
                                        startTime: '10:00',
                                        endTime: '10:30',
                                        reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                                        draftVersion: 1
                                    }
                                }
                            ]
                        }
                    };
                }
                if (chatCallIndex === 2) {
                    return {
                        success: true,
                        message: '',
                        data: {
                            message: 'Đã hủy bản nháp đặt lịch hiện tại.',
                            dialogueOutcome: 'DraftCancelled',
                            primaryIntent: 'CancelDraft',
                            urgency: 'ROUTINE',
                            sessionId: 'sess_a3',
                            draftId: null,
                            bookingDraft: null,
                            actions: []
                        }
                    };
                }
                // chatCallIndex === 3: New draft with IDENTICAL payload after cancel
                return {
                    success: true,
                    message: '',
                    data: {
                        message: 'Bản nháp mới cùng thông tin:',
                        urgency: 'ROUTINE',
                        sessionId: 'sess_a3',
                        draftId: 'draft_a3_second',
                        bookingDraft: {
                            draftId: 'draft_a3_second',
                            specialtyId: 1,
                            doctorId: 101,
                            slotId: 1005,
                            slotDate: '2026-09-24',
                            startTime: '10:00',
                            endTime: '10:30',
                            reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                            isComplete: true,
                            version: 1,
                            confirmationId: 'conf_a3_2'
                        },
                        actions: [
                            {
                                id: 'act-confirm-a3-2',
                                type: 'ConfirmBooking',
                                label: 'Xác nhận bản nháp mới',
                                style: 'primary',
                                requiresAuthentication: true,
                                requiresConfirmation: true,
                                draftVersion: 1,
                                payload: {
                                    confirmationId: 'conf_a3_2',
                                    specialtyId: 1,
                                    doctorId: 101,
                                    slotId: 1005,
                                    slotDate: '2026-09-24',
                                    startTime: '10:00',
                                    endTime: '10:30',
                                    reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                                    draftVersion: 1
                                }
                            }
                        ]
                    }
                };
            }
            if (url === '/appointments') {
                const headers = config?.headers as Record<string, unknown> | undefined;
                const key = String(headers?.['Idempotency-Key'] ?? '');
                capturedKeys.push(key);
                throw new Error('Network timeout');
            }
            return { success: true, message: '', data: {} };
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

        // Turn 1: create draft and confirm -> timeout records K1
        fireEvent.change(input, { target: { value: 'Đặt lịch khám' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const btn1 = await screen.findByRole('button', { name: 'Xác nhận đặt lịch' });
        fireEvent.click(btn1);
        await waitFor(() => expect(capturedKeys).toHaveLength(1));
        const k1 = capturedKeys[0];

        // Turn 2: cancel draft via chat UI/API
        await waitFor(() => expect(screen.getByText(/Network timeout/i)).toBeInTheDocument());
        fireEvent.change(input, { target: { value: 'Hủy đặt lịch' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        await waitFor(() => {
            expect(screen.getByText(/Đã hủy bản nháp đặt lịch hiện tại/i)).toBeInTheDocument();
        });
        expect(sessionStorage.getItem('cliniccare_pending_booking_attempt_pat-1')).toBeNull();

        // Turn 3: create new draft with identical payload -> confirm must use K2 !== K1
        fireEvent.change(input, { target: { value: 'Đặt lịch khám lại' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const btn2 = await screen.findByRole('button', { name: 'Xác nhận bản nháp mới' });
        fireEvent.click(btn2);
        await waitFor(() => expect(capturedKeys).toHaveLength(2));
        expect(capturedKeys[1]).not.toBe(k1);
    });

    it('A5 & A7: double-click sends only 1 request, and switching user while pending isolates key/draft/messages', async () => {
        const capturedKeys: string[] = [];
        let resolveFirstAppointment!: (val: unknown) => void;
        const firstAppointmentDeferred = new Promise(resolve => {
            resolveFirstAppointment = resolve;
        });

        vi.mocked(axiosClient.post).mockImplementation((url, _data, config) => {
            if (url === '/ai/chat') {
                return Promise.resolve({
                    success: true,
                    message: '',
                    data: {
                        message: 'Xác nhận đặt lịch:',
                        urgency: 'ROUTINE',
                        sessionId: 'sess_a5_a7',
                        draftId: 'draft_a5_a7',
                        bookingDraft: {
                            draftId: 'draft_a5_a7',
                            specialtyId: 1,
                            doctorId: 101,
                            slotId: 1005,
                            slotDate: '2026-09-24',
                            startTime: '10:00',
                            endTime: '10:30',
                            reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                            isComplete: true,
                            version: 1,
                            confirmationId: 'conf_a5_a7'
                        },
                        actions: [
                            {
                                id: 'act-confirm-a5-a7',
                                type: 'ConfirmBooking',
                                label: 'Xác nhận đặt lịch',
                                style: 'primary',
                                requiresAuthentication: true,
                                requiresConfirmation: true,
                                draftVersion: 1,
                                payload: {
                                    confirmationId: 'conf_a5_a7',
                                    specialtyId: 1,
                                    doctorId: 101,
                                    slotId: 1005,
                                    slotDate: '2026-09-24',
                                    startTime: '10:00',
                                    endTime: '10:30',
                                    reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                                    draftVersion: 1
                                }
                            }
                        ]
                    }
                });
            }
            if (url === '/appointments') {
                const headers = config?.headers as Record<string, unknown> | undefined;
                capturedKeys.push(String(headers?.['Idempotency-Key'] ?? ''));
                return firstAppointmentDeferred as Promise<unknown>;
            }
            return Promise.resolve({ success: true, message: '', data: {} });
        });

        const { rerender } = render(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Đặt lịch' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const confirmBtn = await screen.findByRole('button', { name: 'Xác nhận đặt lịch' });

        // A7: Rapid double-click on the same ConfirmBooking action
        fireEvent.click(confirmBtn);
        fireEvent.click(confirmBtn);

        await waitFor(() => expect(capturedKeys).toHaveLength(1));

        // A5: Switch authenticated user to pat-2 while pat-1's booking request is still pending
        mockUser = {
            id: 'pat-2',
            fullName: 'Bệnh nhân 2',
            role: 'Patient'
        };
        rerender(
            <MemoryRouter>
                <ChatProvider>
                    <MedicalChatWidget />
                </ChatProvider>
            </MemoryRouter>
        );

        // Now resolve pat-1's in-flight request after user switched to pat-2
        resolveFirstAppointment({
            success: true,
            message: '',
            data: {
                id: 8888,
                appointmentCode: 'APPT-USER1-LEAK-CHECK',
                doctorName: 'BS Nguyễn Văn An',
                specialtyName: 'Tim mạch',
                slotDate: '2026-09-24',
                startTime: '10:00',
                endTime: '10:30',
                reason: 'Triệu chứng đau ngực kéo dài 3 ngày'
            }
        });

        await new Promise(r => setTimeout(r, 30));

        // pat-2 must NOT see pat-1's appointment confirmation or inherit pat-1's key
        expect(screen.queryByText(/APPT-USER1-LEAK-CHECK/)).not.toBeInTheDocument();
        expect(sessionStorage.getItem('cliniccare_pending_booking_attempt_pat-2')).toBeNull();
    });

    it('A6: ConfirmBooking timeout in Widget -> continue same turn on BookAppointment page -> reuses EXACT SAME Idempotency-Key K1', async () => {
        const capturedKeys: string[] = [];

        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url === '/specialties') {
                return {
                    success: true,
                    message: '',
                    data: [{ id: 1, specialtyCode: 'SP01', specialtyName: 'Tim mạch' }]
                };
            }
            if (url === '/specialties/1/doctors' || url.startsWith('/doctors')) {
                if (url.includes('/available-slots')) {
                    return {
                        success: true,
                        message: '',
                        data: [{ id: 1005, slotId: 1005, doctorId: 101, slotDate: '2026-09-24', startTime: '10:00:00', endTime: '10:30:00', isAvailable: true }]
                    };
                }
                return {
                    success: true,
                    message: '',
                    data: [{ id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'ThS.BS', specialtyId: 1, specialtyName: 'Tim mạch' }]
                };
            }
            return { success: true, message: '', data: [] };
        });

        vi.mocked(axiosClient.post).mockImplementation(async (url, _data, config) => {
            if (url === '/ai/chat') {
                return {
                    success: true,
                    message: '',
                    data: {
                        message: 'Xác nhận đặt lịch:',
                        urgency: 'ROUTINE',
                        sessionId: 'sess_a6',
                        draftId: 'draft_a6_shared',
                        bookingDraft: {
                            draftId: 'draft_a6_shared',
                            specialtyId: 1,
                            specialtyName: 'Tim mạch',
                            doctorId: 101,
                            doctorName: 'BS Nguyễn Văn An',
                            slotId: 1005,
                            slotDate: '2026-09-24',
                            startTime: '10:00',
                            endTime: '10:30',
                            reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                            isComplete: true,
                            version: 1,
                            confirmationId: 'conf_a6_shared'
                        },
                        actions: [
                            {
                                id: 'act-confirm-a6',
                                type: 'ConfirmBooking',
                                label: 'Xác nhận đặt lịch',
                                style: 'primary',
                                requiresAuthentication: true,
                                requiresConfirmation: true,
                                draftVersion: 1,
                                payload: {
                                    confirmationId: 'conf_a6_shared',
                                    specialtyId: 1,
                                    specialtyName: 'Tim mạch',
                                    doctorId: 101,
                                    doctorName: 'BS Nguyễn Văn An',
                                    slotId: 1005,
                                    slotDate: '2026-09-24',
                                    startTime: '10:00',
                                    endTime: '10:30',
                                    reason: 'Triệu chứng đau ngực kéo dài 3 ngày',
                                    draftVersion: 1
                                }
                            }
                        ]
                    }
                };
            }
            if (url === '/appointments') {
                const headers = config?.headers as Record<string, unknown> | undefined;
                const key = String(headers?.['Idempotency-Key'] ?? '');
                capturedKeys.push(key);
                if (capturedKeys.length === 1) {
                    throw new Error('Network timeout in widget');
                }
                return {
                    success: true,
                    message: '',
                    data: {
                        id: 9006,
                        appointmentCode: 'APPT-A6-CROSS-SURFACE',
                        doctorName: 'BS Nguyễn Văn An',
                        specialtyName: 'Tim mạch',
                        slotDate: '2026-09-24',
                        startTime: '10:00',
                        endTime: '10:30',
                        reason: 'Triệu chứng đau ngực kéo dài 3 ngày'
                    }
                };
            }
            return { success: true, message: '', data: {} };
        });

        const { unmount } = render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <MedicalChatWidget />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        // Step 1: Confirm in Widget -> fails with timeout, records K1
        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const input = screen.getByLabelText('Nội dung tin nhắn gửi tới ClinicCare AI');
        fireEvent.change(input, { target: { value: 'Đặt lịch khám' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const widgetConfirmBtn = await screen.findByRole('button', { name: 'Xác nhận đặt lịch' });
        fireEvent.click(widgetConfirmBtn);

        await waitFor(() => expect(capturedKeys).toHaveLength(1));
        const k1FromWidget = capturedKeys[0];
        await waitFor(() => expect(screen.getByText(/Network timeout in widget/i)).toBeInTheDocument());

        unmount();

        // Step 2: User continues the exact same draft on BookAppointment page (/patient/book)
        render(
            <MemoryRouter initialEntries={['/patient/book']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        await screen.findByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        const pageConfirmBtn = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });
        fireEvent.click(pageConfirmBtn);

        await waitFor(() => expect(capturedKeys).toHaveLength(2));
        const k2FromBookingPage = capturedKeys[1];

        // Assert BookAppointment page reused the exact same Idempotency-Key K1 generated in Widget!
        expect(k2FromBookingPage).toBe(k1FromWidget);
    });

    it('A4: changing slot or reason invalidates old ConfirmBooking action and uses a new Idempotency-Key for the updated draft', async () => {
        const capturedKeys: string[] = [];
        let chatStep = 0;

        vi.mocked(axiosClient.post).mockImplementation(async (url, _data, config) => {
            if (url === '/ai/chat') {
                chatStep += 1;
                if (chatStep === 1) {
                    return {
                        success: true,
                        message: '',
                        data: {
                            message: 'Bản nháp v1 (09:00):',
                            urgency: 'ROUTINE',
                            sessionId: 'sess_a4',
                            draftId: 'draft_a4',
                            bookingDraft: {
                                draftId: 'draft_a4',
                                specialtyId: 1,
                                doctorId: 101,
                                slotId: 1001,
                                slotDate: '2026-09-24',
                                startTime: '09:00',
                                endTime: '09:30',
                                reason: 'Đau đầu âm ỉ kéo dài 3 ngày',
                                isComplete: true,
                                version: 1,
                                confirmationId: 'conf_a4_v1'
                            },
                            actions: [
                                {
                                    id: 'act-confirm-a4-v1',
                                    type: 'ConfirmBooking',
                                    label: 'Xác nhận đặt lịch v1',
                                    style: 'primary',
                                    requiresAuthentication: true,
                                    requiresConfirmation: true,
                                    draftVersion: 1,
                                    payload: {
                                        confirmationId: 'conf_a4_v1',
                                        specialtyId: 1,
                                        doctorId: 101,
                                        slotId: 1001,
                                        slotDate: '2026-09-24',
                                        startTime: '09:00',
                                        endTime: '09:30',
                                        reason: 'Đau đầu âm ỉ kéo dài 3 ngày',
                                        draftVersion: 1
                                    }
                                }
                            ]
                        }
                    };
                }
                // chatStep === 2: user changed slot to 1002 (10:00), version bumped to v2
                return {
                    success: true,
                    message: '',
                    data: {
                        message: 'Đã cập nhật sang khung giờ 10:00 (v2):',
                        urgency: 'ROUTINE',
                        sessionId: 'sess_a4',
                        draftId: 'draft_a4',
                        bookingDraft: {
                            draftId: 'draft_a4',
                            specialtyId: 1,
                            doctorId: 101,
                            slotId: 1002,
                            slotDate: '2026-09-24',
                            startTime: '10:00',
                            endTime: '10:30',
                            reason: 'Đau đầu kèm chóng mặt buồn nôn',
                            isComplete: true,
                            version: 2,
                            confirmationId: 'conf_a4_v2'
                        },
                        actions: [
                            {
                                id: 'act-confirm-a4-v2',
                                type: 'ConfirmBooking',
                                label: 'Xác nhận đặt lịch v2',
                                style: 'primary',
                                requiresAuthentication: true,
                                requiresConfirmation: true,
                                draftVersion: 2,
                                payload: {
                                    confirmationId: 'conf_a4_v2',
                                    specialtyId: 1,
                                    doctorId: 101,
                                    slotId: 1002,
                                    slotDate: '2026-09-24',
                                    startTime: '10:00',
                                    endTime: '10:30',
                                    reason: 'Đau đầu kèm chóng mặt buồn nôn',
                                    draftVersion: 2
                                }
                            }
                        ]
                    }
                };
            }
            if (url === '/appointments') {
                const headers = config?.headers as Record<string, unknown> | undefined;
                capturedKeys.push(String(headers?.['Idempotency-Key'] ?? ''));
                throw new Error('Temporary network timeout');
            }
            return { success: true, message: '', data: {} };
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

        // 1. Create v1 draft & confirm -> records K1
        fireEvent.change(input, { target: { value: 'Đặt lịch 9h' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const btnV1 = await screen.findByRole('button', { name: 'Xác nhận đặt lịch v1' });
        fireEvent.click(btnV1);
        await waitFor(() => expect(capturedKeys).toHaveLength(1));
        const k1 = capturedKeys[0];

        // 2. User changes slot/reason -> receives v2 draft
        await waitFor(() => expect(screen.getByText(/Temporary network timeout/i)).toBeInTheDocument());
        fireEvent.change(input, { target: { value: 'Đổi sang 10h và cập nhật triệu chứng' } });
        fireEvent.click(screen.getByLabelText('Gửi tin nhắn'));

        const btnV2 = await screen.findByRole('button', { name: 'Xác nhận đặt lịch v2' });

        // Clicking old v1 button is rejected without calling /appointments
        fireEvent.click(btnV1);
        await waitFor(() => {
            expect(screen.getByText(/phiên bản cũ \(v1\)/i)).toBeInTheDocument();
        });
        expect(capturedKeys).toHaveLength(1);

        // Clicking new v2 button calls /appointments with a brand new key K2 !== K1
        fireEvent.click(btnV2);
        await waitFor(() => expect(capturedKeys).toHaveLength(2));
        expect(capturedKeys[1]).not.toBe(k1);
    });

    it('M1: manual booking form (no AI draft) timeout -> unmount/remount ChatProvider & BookAppointment with activeDraft=null -> re-selecting same fields reuses K1 and replays appointment', async () => {
        const capturedKeys: string[] = [];
        const capturedPayloads: unknown[] = [];

        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url === '/specialties') {
                return {
                    success: true,
                    message: '',
                    data: [{ id: 1, specialtyCode: 'SP01', specialtyName: 'Tim mạch' }]
                };
            }
            if (url === '/specialties/1/doctors' || url.startsWith('/doctors')) {
                if (url.includes('/available-slots')) {
                    return {
                        success: true,
                        message: '',
                        data: [
                            { id: 1005, slotId: 1005, doctorId: 101, slotDate: '2026-09-24', startTime: '10:00:00', endTime: '10:30:00', isAvailable: true },
                            { id: 1006, slotId: 1006, doctorId: 101, slotDate: '2026-09-24', startTime: '10:30:00', endTime: '11:00:00', isAvailable: true }
                        ]
                    };
                }
                return {
                    success: true,
                    message: '',
                    data: [{ id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'ThS.BS', specialtyId: 1, specialtyName: 'Tim mạch' }]
                };
            }
            return { success: true, message: '', data: [] };
        });

        vi.mocked(axiosClient.post).mockImplementation(async (url, data, config) => {
            if (url === '/appointments') {
                const headers = config?.headers as Record<string, unknown> | undefined;
                const key = String(headers?.['Idempotency-Key'] ?? '');
                capturedKeys.push(key);
                capturedPayloads.push(data);
                if (capturedKeys.length === 1) {
                    const timeoutErr = new Error('timeout of 15000ms exceeded') as Error & { code?: string };
                    timeoutErr.code = 'ECONNABORTED';
                    throw timeoutErr;
                }
                return {
                    success: true,
                    message: 'Idempotent replay',
                    data: {
                        id: 9901,
                        appointmentCode: 'APPT-MANUAL-REPLAY-9901',
                        doctorName: 'ThS.BS Nguyễn Văn An',
                        specialtyName: 'Tim mạch',
                        slotDate: '2026-09-24',
                        startTime: '10:00',
                        endTime: '10:30',
                        reason: 'Khám tim mạch định kỳ kiểm tra huyết áp'
                    }
                };
            }
            return { success: true, message: '', data: {} };
        });

        // Start with NO AI draft (activeDraft = null)
        sessionStorage.clear();

        const { unmount } = render(
            <MemoryRouter initialEntries={['/patient/book?date=2026-09-24']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        // Step 1: Select Specialty manually
        const specCard = await screen.findByText('Tim mạch');
        fireEvent.click(specCard);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Bác sĩ/i }));

        // Step 2: Select Doctor manually
        const docCard = await screen.findByText(/Nguyễn Văn An/i);
        fireEvent.click(docCard);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Giờ khám/i }));

        // Step 3: Select Slot & Reason manually
        const slotBtn = await screen.findByRole('button', { name: /10:00/i });
        fireEvent.click(slotBtn);
        const reasonInput = screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.change(reasonInput, { target: { value: 'Khám tim mạch định kỳ kiểm tra huyết áp' } });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));

        // Step 4: Submit -> server created appointment, browser receives timeout
        const confirmBtn1 = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });
        fireEvent.click(confirmBtn1);

        await waitFor(() => expect(capturedKeys).toHaveLength(1));
        const k1 = capturedKeys[0];
        expect(k1.length).toBeGreaterThan(5);
        await waitFor(() => expect(screen.getByText(/Lượt đặt lịch trước đó đang chưa rõ kết quả/i)).toBeInTheDocument());

        // Simulate reload where activeDraft is null in sessionStorage (only pending attempt remains)
        unmount();
        sessionStorage.removeItem('cliniccare_booking_draft_pat-1');

        render(
            <MemoryRouter initialEntries={['/patient/book?date=2026-09-24']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        // Uncertain outcome notice is immediately visible on reload even with activeDraft = null
        await waitFor(() => {
            expect(screen.getByText(/Lượt đặt lịch trước đó đang chưa rõ kết quả/i)).toBeInTheDocument();
        });

        // Re-select the exact same specialty, doctor, slot, and reason on the manual form
        const specCard2 = await screen.findByText('Tim mạch');
        fireEvent.click(specCard2);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Bác sĩ/i }));

        const docCard2 = await screen.findByText(/Nguyễn Văn An/i);
        fireEvent.click(docCard2);
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Giờ khám/i }));

        const slotBtn2 = await screen.findByRole('button', { name: /10:00/i });
        fireEvent.click(slotBtn2);
        const reasonInput2 = screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/i);
        fireEvent.change(reasonInput2, { target: { value: 'Khám tim mạch định kỳ kiểm tra huyết áp' } });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));

        const confirmBtn2 = await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i });
        fireEvent.click(confirmBtn2);

        await waitFor(() => expect(capturedKeys).toHaveLength(2));
        expect(capturedKeys[1]).toBe(k1);
        expect(capturedPayloads[1]).toEqual(capturedPayloads[0]);

        await waitFor(() => {
            expect(screen.getAllByText(/APPT-MANUAL-REPLAY-9901/i).length).toBeGreaterThan(0);
        });
    });

    it('M2: manual booking form starting new turn, changing slot/reason, or switching account generates NEW Idempotency-Key and ignores late responses', async () => {
        const capturedKeys: string[] = [];

        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url === '/specialties') {
                return {
                    success: true,
                    message: '',
                    data: [{ id: 1, specialtyCode: 'SP01', specialtyName: 'Tim mạch' }]
                };
            }
            if (url === '/specialties/1/doctors' || url.startsWith('/doctors')) {
                if (url.includes('/available-slots')) {
                    return {
                        success: true,
                        message: '',
                        data: [
                            { id: 1005, slotId: 1005, doctorId: 101, slotDate: '2026-09-24', startTime: '10:00:00', endTime: '10:30:00', isAvailable: true },
                            { id: 1006, slotId: 1006, doctorId: 101, slotDate: '2026-09-24', startTime: '10:30:00', endTime: '11:00:00', isAvailable: true }
                        ]
                    };
                }
                return {
                    success: true,
                    message: '',
                    data: [{ id: 101, fullName: 'Nguyễn Văn An', academicTitle: 'ThS.BS', specialtyId: 1, specialtyName: 'Tim mạch' }]
                };
            }
            return { success: true, message: '', data: [] };
        });

        vi.mocked(axiosClient.post).mockImplementation(async (url, _data, config) => {
            if (url === '/appointments') {
                const headers = config?.headers as Record<string, unknown> | undefined;
                capturedKeys.push(String(headers?.['Idempotency-Key'] ?? ''));
                throw new Error('Network timeout manual M2');
            }
            return { success: true, message: '', data: {} };
        });

        sessionStorage.clear();

        render(
            <MemoryRouter initialEntries={['/patient/book?date=2026-09-24']}>
                <DialogProvider>
                    <ChatProvider>
                        <BookAppointment />
                    </ChatProvider>
                </DialogProvider>
            </MemoryRouter>
        );

        // 1. First manual booking attempt -> timeout records K1
        fireEvent.click(await screen.findByText('Tim mạch'));
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Bác sĩ/i }));
        fireEvent.click(await screen.findByText(/Nguyễn Văn An/i));
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Giờ khám/i }));
        fireEvent.click(await screen.findByRole('button', { name: /10:00/i }));
        fireEvent.change(screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/i), {
            target: { value: 'Khám tim mạch định kỳ kiểm tra huyết áp' }
        });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        fireEvent.click(await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i }));

        await waitFor(() => expect(capturedKeys).toHaveLength(1));
        const k1 = capturedKeys[0];

        // 2. Click "Hủy & bắt đầu lượt đặt lịch mới" -> explicitly starts a new manual turn
        const startNewTurnBtn = await screen.findByRole('button', { name: /Hủy & bắt đầu lượt đặt lịch mới/i });
        fireEvent.click(startNewTurnBtn);
        expect(sessionStorage.getItem('cliniccare_pending_booking_attempt_pat-1')).toBeNull();

        // Re-select the EXACT same payload in the new turn -> must generate K2 !== K1
        fireEvent.click(await screen.findByText('Tim mạch'));
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Bác sĩ/i }));
        fireEvent.click(await screen.findByText(/Nguyễn Văn An/i));
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn Giờ khám/i }));
        fireEvent.click(await screen.findByRole('button', { name: /10:00/i }));
        fireEvent.change(screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/i), {
            target: { value: 'Khám tim mạch định kỳ kiểm tra huyết áp' }
        });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        fireEvent.click(await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i }));

        await waitFor(() => expect(capturedKeys).toHaveLength(2));
        const k2 = capturedKeys[1];
        expect(k2).not.toBe(k1);

        // 3. Change reason/slot without clicking reset -> must generate K3 !== K2 and overwrite K2
        fireEvent.click(screen.getByRole('button', { name: /Quay lại/i }));
        fireEvent.change(screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/i), {
            target: { value: 'Triệu chứng mới: khó thở khi gắng sức' }
        });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/i }));
        fireEvent.click(await screen.findByRole('button', { name: /Xác nhận & Đặt lịch/i }));

        await waitFor(() => expect(capturedKeys).toHaveLength(3));
        const k3 = capturedKeys[2];
        expect(k3).not.toBe(k2);
        expect(k3).not.toBe(k1);
    });
});
