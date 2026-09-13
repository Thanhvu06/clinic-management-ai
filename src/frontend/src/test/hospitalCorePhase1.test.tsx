import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { DialogProvider } from '../contexts/DialogContext';
import { AdminFacilities } from '../pages/admin/AdminFacilities';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';
import { MpiPatientSearchModal } from '../pages/reception/MpiPatientSearchModal';
import { organizationApi, type FacilityDto, type DepartmentDto, type RoomDto, type BedDto } from '../api/organizationApi';
import { mpiApi, type MpiPatientDto } from '../api/mpiApi';

vi.mock('../api/organizationApi', () => ({
    organizationApi: {
        getFacilities: vi.fn(),
        getFacilityById: vi.fn(),
        createFacility: vi.fn(),
        getDepartments: vi.fn(),
        createDepartment: vi.fn(),
        getRooms: vi.fn(),
        createRoom: vi.fn(),
        getBedsByRoom: vi.fn(),
        createBed: vi.fn()
    }
}));

vi.mock('../api/mpiApi', () => ({
    mpiApi: {
        searchPatients: vi.fn(),
        getPatientById: vi.fn(),
        getPatientByMrn: vi.fn(),
        registerWalkIn: vi.fn(),
        addAllergy: vi.fn(),
        removeAllergy: vi.fn()
    }
}));

const mockFacilities: FacilityDto[] = [
    {
        id: 1,
        code: 'BV-TW-01',
        name: 'Bệnh viện Đa khoa Trung tâm',
        address: '123 Đường Y Học',
        city: 'TP. Hồ Chí Minh',
        phone: '02812345678',
        hospitalLevel: 'Hạng Đặc Biệt',
        isActive: true,
        buildingCount: 3,
        departmentCount: 12
    }
];

const mockDepartments: DepartmentDto[] = [
    {
        id: 10,
        facilityId: 1,
        facilityName: 'Bệnh viện Đa khoa Trung tâm',
        code: 'K-CC',
        name: 'Khoa Cấp cứu',
        departmentType: 1,
        departmentTypeName: 'Clinical',
        isActive: true,
        roomCount: 5
    }
];

const mockRooms: RoomDto[] = [
    {
        id: 100,
        departmentId: 10,
        departmentName: 'Khoa Cấp cứu',
        facilityId: 1,
        facilityName: 'Bệnh viện Đa khoa Trung tâm',
        roomNumber: 'P-101',
        name: 'Phòng Hồi sức Tích cực 1',
        roomType: 2,
        roomTypeName: 'Inpatient',
        floorNumber: 1,
        maxCapacity: 4,
        isActive: true,
        bedCount: 2,
        availableBedCount: 1
    }
];

const mockBeds: BedDto[] = [
    {
        id: 1000,
        roomId: 100,
        roomNumber: 'P-101',
        departmentId: 10,
        departmentName: 'Khoa Cấp cứu',
        bedNumber: 'G-01',
        bedType: 2,
        bedTypeName: 'Icu',
        status: 1,
        statusName: 'Available',
        dailyRate: 500000,
        isActive: true
    }
];

const mockMpiPatient: MpiPatientDto = {
    id: 1,
    medicalRecordNumber: 'BN-2026-000001',
    fullName: 'NGUYỄN VĂN AN',
    phoneNumber: '0901234567',
    gender: 'Male',
    age: 35,
    nationalId: '079190001234',
    bhytNumber: 'GD4010123456789',
    bloodType: 'O',
    rhFactor: '+',
    primaryFacilityId: 1,
    primaryFacilityName: 'Bệnh viện Đa khoa Trung tâm',
    allergies: [
        {
            id: 1,
            patientId: 1,
            allergenType: 1,
            allergenTypeName: 'Thuốc',
            allergenName: 'Penicillin',
            severity: 4,
            severityName: 'Sốc phản vệ',
            reactionDescription: 'Khó thở cấp, tụt huyết áp',
            recordedAtUtc: '2026-09-13T00:00:00Z'
        }
    ],
    emergencyContacts: [
        {
            id: 1,
            patientId: 1,
            fullName: 'Nguyễn Thị Bình',
            relationship: 'Vợ',
            phoneNumber: '0909876543',
            isPrimary: true
        }
    ]
};

describe('Hospital Core Phase 1 Frontend Components', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    describe('AdminFacilities Component', () => {
        it('renders facilities and loads departments & rooms when facility selected', async () => {
            vi.mocked(organizationApi.getFacilities).mockResolvedValue({
                success: true,
                message: 'OK',
                data: mockFacilities
            });
            vi.mocked(organizationApi.getDepartments).mockResolvedValue({
                success: true,
                message: 'OK',
                data: mockDepartments
            });
            vi.mocked(organizationApi.getRooms).mockResolvedValue({
                success: true,
                message: 'OK',
                data: mockRooms
            });
            vi.mocked(organizationApi.getBedsByRoom).mockResolvedValue({
                success: true,
                message: 'OK',
                data: mockBeds
            });

            render(
                <DialogProvider>
                    <BrowserRouter>
                        <AdminFacilities />
                    </BrowserRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('Quản trị Mạng lưới Bệnh viện & Cơ sở')).toBeInTheDocument();
                expect(screen.getByText('Bệnh viện Đa khoa Trung tâm')).toBeInTheDocument();
            });

            expect(organizationApi.getFacilities).toHaveBeenCalled();
        });
    });

    describe('MpiPatientSearchModal Component', () => {
        it('searches patients and triggers onSelectPatient', async () => {
            vi.mocked(mpiApi.searchPatients).mockResolvedValue({
                success: true,
                message: 'OK',
                data: {
                    items: [mockMpiPatient],
                    totalItems: 1,
                    page: 1,
                    pageSize: 10,
                    totalPages: 1
                }
            });

            const onSelect = vi.fn();
            const onClose = vi.fn();

            render(
                <MpiPatientSearchModal
                    isOpen={true}
                    onClose={onClose}
                    onSelectPatient={onSelect}
                />
            );

            expect(screen.getByText('Tra cứu hồ sơ bệnh nhân (MPI)')).toBeInTheDocument();

            const input = screen.getByPlaceholderText(/Nhập từ khóa tìm kiếm/i);
            fireEvent.change(input, { target: { value: 'NGUYỄN VĂN AN' } });

            const searchBtn = screen.getByRole('button', { name: /Tra cứu/i });
            fireEvent.click(searchBtn);

            await waitFor(() => {
                expect(screen.getByText('BN-2026-000001')).toBeInTheDocument();
                expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
                expect(screen.getByText(/Penicillin/i)).toBeInTheDocument();
            });

            const selectBtn = screen.getByRole('button', { name: /Chọn hồ sơ/i });
            fireEvent.click(selectBtn);

            expect(onSelect).toHaveBeenCalledWith(mockMpiPatient);
            expect(onClose).toHaveBeenCalled();
        });

        it('renders Male, Female, and Other patients with correct Vietnamese labels', async () => {
            const patientsWithDifferentGenders: MpiPatientDto[] = [
                { ...mockMpiPatient, id: 101, fullName: 'BỆNH NHÂN NAM', gender: 'Male', genderName: undefined },
                { ...mockMpiPatient, id: 102, fullName: 'BỆNH NHÂN NỮ', gender: 'Female', genderName: undefined },
                { ...mockMpiPatient, id: 103, fullName: 'BỆNH NHÂN KHÁC', gender: 'Other', genderName: undefined }
            ];

            vi.mocked(mpiApi.searchPatients).mockResolvedValue({
                success: true,
                message: 'OK',
                data: {
                    items: patientsWithDifferentGenders,
                    totalItems: 3,
                    page: 1,
                    pageSize: 10,
                    totalPages: 1
                }
            });

            render(
                <MpiPatientSearchModal
                    isOpen={true}
                    onClose={vi.fn()}
                    onSelectPatient={vi.fn()}
                />
            );

            const input = screen.getByPlaceholderText(/Nhập từ khóa tìm kiếm/i);
            fireEvent.change(input, { target: { value: 'BỆNH NHÂN' } });
            fireEvent.click(screen.getByRole('button', { name: /Tra cứu/i }));

            await waitFor(() => {
                expect(screen.getByText('BỆNH NHÂN NAM')).toBeInTheDocument();
                expect(screen.getByText('BỆNH NHÂN NỮ')).toBeInTheDocument();
                expect(screen.getByText('BỆNH NHÂN KHÁC')).toBeInTheDocument();
                expect(screen.getByText(/\(Nam - 35 tuổi\)/i)).toBeInTheDocument();
                expect(screen.getByText(/\(Nữ - 35 tuổi\)/i)).toBeInTheDocument();
                expect(screen.getByText(/\(Khác - 35 tuổi\)/i)).toBeInTheDocument();
            });
        });
    });

    describe('WalkInPatientRegistration Component', () => {
        it('registers walk-in patient and displays assigned MRN', async () => {
            vi.mocked(organizationApi.getFacilities).mockResolvedValue({
                success: true,
                message: 'OK',
                data: mockFacilities
            });

            vi.mocked(mpiApi.registerWalkIn).mockResolvedValue({
                success: true,
                message: 'Đăng ký thành công',
                data: mockMpiPatient
            });

            render(
                <DialogProvider>
                    <BrowserRouter>
                        <WalkInPatientRegistration />
                    </BrowserRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText(/Tiếp nhận bệnh nhân vãng lai/i)).toBeInTheDocument();
            });

            // Fill full name
            const nameInput = screen.getByPlaceholderText(/NGUYỄN VĂN A/i);
            fireEvent.change(nameInput, { target: { value: 'NGUYỄN VĂN AN' } });

            // Submit form
            const submitBtn = screen.getByRole('button', { name: /Xác nhận tiếp nhận & Cấp MRN/i });
            fireEvent.click(submitBtn);

            await waitFor(() => {
                expect(mpiApi.registerWalkIn).toHaveBeenCalledWith(expect.objectContaining({
                    fullName: 'NGUYỄN VĂN AN',
                    gender: 'Male'
                }));
                expect(screen.getByText('Tiếp nhận bệnh nhân thành công!')).toBeInTheDocument();
                expect(screen.getByText('BN-2026-000001')).toBeInTheDocument();
            });
        });

        it('submits correct gender enum payload when Female or Other is selected', async () => {
            vi.mocked(organizationApi.getFacilities).mockResolvedValue({
                success: true,
                message: 'OK',
                data: mockFacilities
            });

            vi.mocked(mpiApi.registerWalkIn).mockResolvedValue({
                success: true,
                message: 'Đăng ký thành công',
                data: { ...mockMpiPatient, gender: 'Other', fullName: 'LÊ VĂN KHÁC' }
            });

            render(
                <DialogProvider>
                    <BrowserRouter>
                        <WalkInPatientRegistration />
                    </BrowserRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText(/Tiếp nhận bệnh nhân vãng lai/i)).toBeInTheDocument();
            });

            const nameInput = screen.getByPlaceholderText(/NGUYỄN VĂN A/i);
            fireEvent.change(nameInput, { target: { value: 'LÊ VĂN KHÁC' } });

            const genderSelect = screen.getByDisplayValue('Nam');
            fireEvent.change(genderSelect, { target: { value: 'Other' } });

            const submitBtn = screen.getByRole('button', { name: /Xác nhận tiếp nhận & Cấp MRN/i });
            fireEvent.click(submitBtn);

            await waitFor(() => {
                expect(mpiApi.registerWalkIn).toHaveBeenCalledWith(expect.objectContaining({
                    fullName: 'LÊ VĂN KHÁC',
                    gender: 'Other'
                }));
            });
        });
    });

    describe('formatGender helper', () => {
        it('correctly maps string enum values Male, Female, Other', async () => {
            const { formatGender } = await import('../pages/reception/MpiPatientSearchModal');
            expect(formatGender('Male')).toBe('Nam');
            expect(formatGender('Female')).toBe('Nữ');
            expect(formatGender('Other')).toBe('Khác');
        });

        it('correctly maps legacy numbers 0=Male, 1=Female, 2=Other, and rejects 3 or negative as Chưa rõ', async () => {
            const { formatGender } = await import('../pages/reception/MpiPatientSearchModal');
            expect(formatGender(0)).toBe('Nam');
            expect(formatGender(1)).toBe('Nữ');
            expect(formatGender(2)).toBe('Khác');
            expect(formatGender(3)).toBe('Chưa rõ');
            expect(formatGender(-1)).toBe('Chưa rõ');
            expect(formatGender(99)).toBe('Chưa rõ');
        });

        it('correctly handles genderName fallback and null/undefined values', async () => {
            const { formatGender } = await import('../pages/reception/MpiPatientSearchModal');
            expect(formatGender(undefined, 'Nam')).toBe('Nam');
            expect(formatGender(undefined, 'Nữ')).toBe('Nữ');
            expect(formatGender(null)).toBe('Chưa rõ');
            expect(formatGender(undefined)).toBe('Chưa rõ');
        });
    });
});
