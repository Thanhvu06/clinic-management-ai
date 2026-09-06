import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { ReceptionChangeRequests } from '../pages/reception/ReceptionChangeRequests';
import { DialogProvider } from '../contexts/DialogContext';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({
    default: {
        get: vi.fn(),
        post: vi.fn(),
    }
}));

const mockAppointments = [
    {
        id: 101,
        appointmentCode: 'APT-101',
        patientId: 1,
        doctorId: 1,
        doctorName: 'BS.CKI Nguyễn Minh Khải',
        specialtyId: 1,
        appointmentSlotId: 10,
        slotDate: '2026-09-10T00:00:00',
        startTime: '09:00:00',
        endTime: '09:30:00',
        status: 'Confirmed',
        symptoms: 'Đau đầu, chóng mặt'
    },
    {
        id: 102,
        appointmentCode: 'APT-102',
        patientId: 1,
        doctorId: 1,
        doctorName: 'BS.CKI Nguyễn Minh Khải',
        specialtyId: 1,
        appointmentSlotId: 11,
        slotDate: '2026-09-12T00:00:00',
        startTime: '10:00:00',
        endTime: '10:30:00',
        status: 'PendingReschedule',
        symptoms: 'Tái khám'
    }
];

const mockSpecialties = [
    { id: 1, specialtyName: 'Nội tổng quát', name: 'Nội tổng quát' }
];

const mockAvailableSlots = [
    { slotId: 201, doctorId: 1, slotDate: '2026-09-15', startTime: '14:00:00', endTime: '14:30:00' },
    { slotId: 202, doctorId: 1, slotDate: '2026-09-15', startTime: '14:30:00', endTime: '15:00:00' }
];

const mockPendingRequests = [
    {
        id: 501,
        appointmentId: 102,
        requestType: 'Reschedule',
        requestedSlotId: 201,
        reason: 'Bận đột xuất',
        status: 'Pending',
        requestedSlotDate: '2026-09-15',
        requestedStartTime: '14:00:00',
        requestedEndTime: '14:30:00'
    }
];

const mockChangeRequestsList = [
    {
        id: 501,
        appointmentId: 102,
        appointmentCode: 'APT-102',
        patientName: 'Nguyễn Văn A',
        doctorName: 'BS.CKI Nguyễn Minh Khải',
        specialtyName: 'Nội tổng quát',
        requestType: 'Reschedule',
        requestedSlotId: 201,
        requestedSlotDate: '2026-09-15',
        requestedStartTime: '14:00:00',
        requestedEndTime: '14:30:00',
        reason: 'Bận đột xuất xin đổi giờ',
        status: 'Pending',
        createdAt: '2026-09-06T10:00:00Z'
    },
    {
        id: 502,
        appointmentId: 103,
        appointmentCode: 'APT-103',
        patientName: 'Trần Thị B',
        doctorName: 'BS.CKI Nguyễn Minh Khải',
        specialtyName: 'Nội tổng quát',
        requestType: 'Cancellation',
        requestedSlotId: null,
        reason: 'Có việc gia đình',
        status: 'Pending',
        createdAt: '2026-09-06T11:00:00Z'
    }
];

describe('Appointment Change Request UI Workflows', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    describe('PatientAppointments Component', () => {
        it('renders patient appointments and displays action buttons for confirmed appointments', async () => {
            vi.mocked(axiosClient.get).mockImplementation((url: string) => {
                if (url.includes('/specialties')) {
                    return Promise.resolve({ success: true, data: mockSpecialties });
                }
                if (url.includes('/appointments/my')) {
                    return Promise.resolve({ success: true, data: { items: mockAppointments, totalItems: 2 } });
                }
                if (url.includes('/appointment-change-requests')) {
                    return Promise.resolve({ success: true, data: { items: mockPendingRequests, totalItems: 1 } });
                }
                return Promise.resolve({ success: true, data: [] });
            });

            render(
                <DialogProvider>
                    <BrowserRouter>
                        <PatientAppointments />
                    </BrowserRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('#APT-101')).toBeInTheDocument();
                expect(screen.getByText('#APT-102')).toBeInTheDocument();
            });

            // Action buttons for APT-101 (Confirmed)
            expect(screen.getByText('Đổi lịch')).toBeInTheDocument();
            expect(screen.getByText('Hủy lịch')).toBeInTheDocument();

            // Status badge and withdraw button for APT-102 (PendingReschedule)
            expect(screen.getByText('Chờ đổi lịch')).toBeInTheDocument();
            expect(screen.getByText('Rút yêu cầu')).toBeInTheDocument();
        });

        it('opens reschedule modal, selects a slot, enters reason, and submits request', async () => {
            vi.mocked(axiosClient.get).mockImplementation((url: string) => {
                if (url.includes('/specialties')) return Promise.resolve({ success: true, data: mockSpecialties });
                if (url.includes('/appointments/my')) return Promise.resolve({ success: true, data: { items: mockAppointments, totalItems: 2 } });
                if (url.includes('/appointment-change-requests')) return Promise.resolve({ success: true, data: { items: [], totalItems: 0 } });
                if (url.includes('/available-slots')) return Promise.resolve({ success: true, data: mockAvailableSlots });
                return Promise.resolve({ success: true, data: [] });
            });

            vi.mocked(axiosClient.post).mockResolvedValue({ success: true, data: { id: 601 } });

            render(
                <DialogProvider>
                    <BrowserRouter>
                        <PatientAppointments />
                    </BrowserRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('#APT-101')).toBeInTheDocument();
            });

            // Click "Đổi lịch"
            fireEvent.click(screen.getByText('Đổi lịch'));

            // Modal appears
            await waitFor(() => {
                expect(screen.getByText('Đổi lịch khám #APT-101')).toBeInTheDocument();
            });

            // Wait for slots to load
            await waitFor(() => {
                expect(screen.getByText('14:00 - 14:30')).toBeInTheDocument();
            });

            // Select slot
            fireEvent.click(screen.getByText('14:00 - 14:30'));

            // Enter reason
            const textarea = screen.getByPlaceholderText(/Vui lòng nhập lý do/i);
            fireEvent.change(textarea, { target: { value: 'Tôi bận việc sáng, muốn chuyển chiều' } });

            // Submit
            const submitBtn = screen.getByText('Gửi yêu cầu dời lịch');
            fireEvent.click(submitBtn);

            await waitFor(() => {
                expect(axiosClient.post).toHaveBeenCalledWith(
                    '/appointments/101/reschedule-requests',
                    expect.objectContaining({
                        requestedSlotId: 201,
                        reason: 'Tôi bận việc sáng, muốn chuyển chiều'
                    })
                );
            });
        });
    });

    describe('ReceptionChangeRequests Component', () => {
        it('renders change requests and filters by type and status', async () => {
            vi.mocked(axiosClient.get).mockImplementation((url: string) => {
                if (url.includes('/reception/change-requests')) {
                    return Promise.resolve({ success: true, data: { items: mockChangeRequestsList, totalItems: 2 } });
                }
                return Promise.resolve({ success: true, data: [] });
            });

            render(
                <DialogProvider>
                    <BrowserRouter>
                        <ReceptionChangeRequests />
                    </BrowserRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('#APT-102')).toBeInTheDocument();
                expect(screen.getByText('#APT-103')).toBeInTheDocument();
                expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument();
            });

            // Check filters exist
            expect(screen.getByText('Tất cả loại yêu cầu')).toBeInTheDocument();
            expect(screen.getByText('Tất cả trạng thái')).toBeInTheDocument();
        });

        it('opens detail modal and can approve reschedule', async () => {
            vi.mocked(axiosClient.get).mockImplementation((url: string) => {
                if (url.includes('/reception/change-requests')) {
                    return Promise.resolve({ success: true, data: { items: mockChangeRequestsList, totalItems: 2 } });
                }
                if (url.includes('/reception/appointments/102')) {
                    return Promise.resolve({
                        success: true,
                        data: {
                            patientName: 'Nguyễn Văn A',
                            patientPhone: '0912345678',
                            doctorName: 'BS.CKI Nguyễn Minh Khải',
                            specialtyName: 'Nội tổng quát',
                            appointmentDate: '2026-09-10',
                            startTime: '09:00:00',
                            endTime: '09:30:00'
                        }
                    });
                }
                return Promise.resolve({ success: true, data: [] });
            });

            vi.mocked(axiosClient.post).mockResolvedValue({ success: true });

            render(
                <DialogProvider>
                    <BrowserRouter>
                        <ReceptionChangeRequests />
                    </BrowserRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('#APT-102')).toBeInTheDocument();
            });

            // Click "Xử lý" on first item
            const processButtons = screen.getAllByText('Xử lý');
            fireEvent.click(processButtons[0]);

            await waitFor(() => {
                expect(screen.getByText(/Chi tiết yêu cầu/i)).toBeInTheDocument();
                expect(screen.getByText('Duyệt đổi lịch')).toBeInTheDocument();
                expect(screen.getByText('Từ chối yêu cầu')).toBeInTheDocument();
            });
        });
    });
});
