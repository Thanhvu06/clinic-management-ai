import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';
import { organizationApi } from '../api/organizationApi';
import { patientVisitApi } from '../api/patientVisitApi';
import axiosClient from '../api/axiosClient';
import { DialogProvider } from '../contexts/DialogContext';

vi.mock('../api/organizationApi', () => ({
    organizationApi: {
        getFacilities: vi.fn(),
        getDepartments: vi.fn(),
        getRooms: vi.fn(),
        getStaffAssignments: vi.fn(),
        createStaffAssignment: vi.fn(),
        deleteStaffAssignment: vi.fn(),
    },
}));

vi.mock('../api/patientVisitApi', () => ({
    patientVisitApi: {
        receptionIntake: vi.fn(),
        getDepartmentQueue: vi.fn(),
    },
}));

vi.mock('../api/mpiApi', () => ({
    mpiApi: {
        searchPatients: vi.fn(),
        getPatientById: vi.fn(),
    },
}));

vi.mock('../api/axiosClient', () => ({
    default: {
        get: vi.fn(),
        post: vi.fn(),
    },
}));

const mockFacilities = [
    { id: 1, code: 'FAC-01', name: 'Cơ sở Quận 1 - Trung tâm', address: '123 Nguyễn Huệ', isActive: true },
    { id: 2, code: 'FAC-02', name: 'Cơ sở Quận 7 - Nam Sài Gòn', address: '456 Nguyễn Lương Bằng', isActive: true },
];

const mockAppointments = [
    {
        id: 101,
        appointmentCode: 'APT-101',
        patientId: 1,
        patientName: 'Nguyễn Văn An',
        patientPhone: '0901234567',
        medicalRecordNumber: 'BN-2026-000001',
        nationalId: '079199000111',
        doctorId: 10,
        doctorName: 'BS. Lê Trọng Nghĩa',
        specialtyId: 2,
        specialtyName: 'Tim mạch',
        appointmentDate: new Date().toISOString().split('T')[0],
        startTime: '08:30',
        endTime: '09:00',
        reason: 'Khám kiểm tra huyết áp',
        status: 'Confirmed',
    },
    {
        id: 102,
        appointmentCode: 'APT-102',
        patientId: 2,
        patientName: 'Trần Thị Bình',
        patientPhone: '0902345678',
        medicalRecordNumber: 'BN-2026-000002',
        nationalId: '079199000222',
        doctorId: 10,
        doctorName: 'BS. Lê Trọng Nghĩa',
        specialtyId: 2,
        specialtyName: 'Tim mạch',
        appointmentDate: new Date().toISOString().split('T')[0],
        startTime: '09:00',
        endTime: '09:30',
        reason: 'Tái khám định kỳ',
        status: 'Completed',
    }
];

describe('Reception Workspace Rebuild', () => {
    beforeEach(() => {
        vi.clearAllMocks();

        vi.mocked(organizationApi.getFacilities).mockResolvedValue({
            success: true,
            data: mockFacilities,
        } as any);

        vi.mocked(organizationApi.getDepartments).mockResolvedValue({
            success: true,
            data: [
                { id: 10, code: 'K01', name: 'Khoa Nội Tổng Quát', facilityId: 1, isActive: true },
            ],
        } as any);

        vi.mocked(organizationApi.getRooms).mockResolvedValue({
            success: true,
            data: [
                { id: 101, roomNumber: 'P101', name: 'Phòng 101', departmentId: 10, isActive: true },
            ],
        } as any);

        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url.includes('/reception/appointments')) {
                return Promise.resolve({
                    success: true,
                    data: {
                        items: mockAppointments,
                        totalCount: 2,
                        pageNumber: 1,
                        pageSize: 20,
                        totalPages: 1,
                    },
                } as any);
            }
            if (url.includes('/reception/stats')) {
                return Promise.resolve({
                    success: true,
                    data: {
                        appointmentsToday: 14,
                        pendingAppointmentsToday: 3,
                        confirmedAppointmentsToday: 5,
                        completedAppointmentsToday: 6,
                    },
                } as any);
            }
            if (url.includes('/doctors')) {
                return Promise.resolve({
                    success: true,
                    data: [
                        { id: 10, fullName: 'BS. Lê Trọng Nghĩa', specialtyName: 'Tim mạch' }
                    ]
                } as any);
            }
            return Promise.resolve({ success: true, data: [] } as any);
        });
    });

    it('renders workspace topbar, facility selector, metric cards and appointment table', async () => {
        render(
            <MemoryRouter>
                <DialogProvider>
                    <ReceptionWorkspace />
                </DialogProvider>
            </MemoryRouter>
        );

        // Header
        expect(screen.getByRole('heading', { name: /Bàn Làm Việc Lễ Tân/i })).toBeInTheDocument();

        // Facilities loaded
        await waitFor(() => {
            expect(organizationApi.getFacilities).toHaveBeenCalled();
        });

        // Metric cards rendered
        await waitFor(() => {
            expect(screen.getByText('Lịch khám hôm nay')).toBeInTheDocument();
            expect(screen.getByText('Chờ tiếp đón / Đang khám')).toBeInTheDocument();
            expect(screen.getByText('Đã khám xong hôm nay')).toBeInTheDocument();
        });

        // Action panel quick links
        expect(screen.getByText('Tiếp nhận người bệnh mới')).toBeInTheDocument();
        expect(screen.getByText('Mở thu ngân viện phí')).toBeInTheDocument();
        expect(screen.getByText('Quản lý đăng ký gói khám')).toBeInTheDocument();

        // Appointment table items
        await waitFor(() => {
            expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
            expect(screen.getByText('Trần Thị Bình')).toBeInTheDocument();
            expect(screen.getByText(/BN-2026-000001/)).toBeInTheDocument();
        });
    });

    it('filters worklist by appointment tabs', async () => {
        render(
            <MemoryRouter>
                <DialogProvider>
                    <ReceptionWorkspace />
                </DialogProvider>
            </MemoryRouter>
        );

        // Check tabs exist
        const upcomingTab = screen.getByRole('button', { name: /Sắp tới/i });
        const historyTab = screen.getByRole('button', { name: /Lịch sử/i });

        expect(upcomingTab).toBeInTheDocument();
        expect(historyTab).toBeInTheDocument();

        // Click upcoming tab
        fireEvent.click(upcomingTab);

        await waitFor(() => {
            expect(axiosClient.get).toHaveBeenCalledWith(
                expect.stringContaining('tab=upcoming')
            );
        });
    });

    it('supports 3-step walk-in intake flow with idempotency key and emergency contact fallback', async () => {
        vi.mocked(patientVisitApi.receptionIntake).mockResolvedValue({
            success: true,
            data: {
                visitId: 501,
                visitCode: 'VIS-20260914-0001',
                queueNumber: 1,
                queueDisplay: '01',
                patientId: 99,
                patientName: 'Bé Nguyễn Cún',
                medicalRecordNumber: 'BN-2026-000099',
                phoneNumber: '0988112233',
                facilityId: 1,
                facilityName: 'Cơ sở Quận 1 - Trung tâm',
                departmentId: 10,
                departmentName: 'Khoa Nội Tổng Quát',
                roomId: 101,
                roomNumber: 'P101',
                assignedDoctorId: 10,
                doctorName: 'BS. Lê Trọng Nghĩa',
                checkedInAtUtc: new Date().toISOString(),
                receptionistName: 'Lễ tân',
                status: 'WaitingForDoctor',
                priority: 'Normal',
                arrivalType: 'WalkIn',
            },
        } as any);

        const { container } = render(
            <MemoryRouter>
                <DialogProvider>
                    <WalkInPatientRegistration />
                </DialogProvider>
            </MemoryRouter>
        );

        // Step 1: Default is "Tra cứu hồ sơ cũ" vs "Đăng ký hồ sơ người bệnh mới"
        expect(screen.getByText('Tìm & Chọn Người Bệnh')).toBeInTheDocument();
        const registerNewTab = screen.getByText(/Đăng ký hồ sơ người bệnh mới/i);
        fireEvent.click(registerNewTab);

        // Fill new patient profile without personal phone, but with emergency contact phone
        const fullNameInput = screen.getByPlaceholderText('VD: NGUYỄN VĂN A');
        fireEvent.change(fullNameInput, { target: { value: 'Bé Nguyễn Cún' } });

        const dobInput = container.querySelector('input[type="date"]')!;
        fireEvent.change(dobInput, { target: { value: '2022-06-15' } });

        const ecNameInput = screen.getByPlaceholderText('VD: Trần Thị B');
        fireEvent.change(ecNameInput, { target: { value: 'Mẹ Hoàng Thị Hoa' } });

        const phoneInputs = screen.getAllByPlaceholderText('09xx xxx xxx');
        // phoneInputs[1] is the emergency contact phone
        fireEvent.change(phoneInputs[1], { target: { value: '0988112233' } });

        // Advance to Step 2
        const nextButton = screen.getByText(/Tiếp tục: Chuẩn bị lượt khám/i);
        fireEvent.click(nextButton);

        await waitFor(() => {
            expect(screen.getByText('Chuẩn Bị Lượt Khám')).toBeInTheDocument();
        });

        // Fill chief complaint
        const reasonInput = screen.getByPlaceholderText(/Mô tả lý do đến khám/i);
        fireEvent.change(reasonInput, { target: { value: 'Bé bị ho sốt 2 ngày' } });

        // Advance to Step 3
        const nextToConfirm = screen.getByText(/Tiếp tục: Xác nhận thông tin/i);
        fireEvent.click(nextToConfirm);

        await waitFor(() => {
            expect(screen.getByText('Xác Nhận & Cấp STT')).toBeInTheDocument();
            expect(screen.getByText('Bé Nguyễn Cún')).toBeInTheDocument();
        });

        // Submit intake
        const submitBtn = screen.getByText(/Xác nhận tiếp nhận & Cấp STT/i);
        fireEvent.click(submitBtn);

        await waitFor(() => {
            expect(patientVisitApi.receptionIntake).toHaveBeenCalledWith(
                expect.objectContaining({
                    facilityId: 1,
                    newPatient: expect.objectContaining({
                        fullName: 'Bé Nguyễn Cún',
                        emergencyContact: expect.objectContaining({
                            phoneNumber: '0988112233',
                        }),
                    }),
                }),
                expect.any(String) // Idempotency key
            );
        });
    });

    it('handles 0 facilities by displaying unassigned banner and notice', async () => {
        vi.mocked(organizationApi.getFacilities).mockResolvedValue({
            success: true,
            data: [],
        } as any);

        render(
            <MemoryRouter>
                <DialogProvider>
                    <ReceptionWorkspace />
                </DialogProvider>
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Chưa được phân công cơ sở trực')).toBeInTheDocument();
            expect(screen.getByText(/Tài khoản chưa được phân công cơ sở trực/)).toBeInTheDocument();
        });
    });

    it('handles 1 facility by rendering static badge without select dropdown', async () => {
        vi.mocked(organizationApi.getFacilities).mockResolvedValue({
            success: true,
            data: [mockFacilities[0]],
        } as any);

        render(
            <MemoryRouter>
                <DialogProvider>
                    <ReceptionWorkspace />
                </DialogProvider>
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Cơ sở Quận 1 - Trung tâm (FAC-01)')).toBeInTheDocument();
        });
        expect(screen.queryByRole('combobox')).toBeNull();
    });

    it('displays error message and retry button on initial load failure', async () => {
        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url.includes('/reception/appointments')) {
                return Promise.reject(new Error('Network error'));
            }
            return Promise.resolve({ success: true, data: {} } as any);
        });

        render(
            <MemoryRouter>
                <DialogProvider>
                    <ReceptionWorkspace />
                </DialogProvider>
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Không thể tải danh sách lịch tiếp nhận. Vui lòng thử lại.')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /Thử lại/i })).toBeInTheDocument();
        });
    });
});

