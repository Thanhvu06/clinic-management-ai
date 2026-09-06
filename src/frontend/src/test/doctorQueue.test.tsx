import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { DoctorQueue } from '../pages/doctor/DoctorQueue';
import { DialogProvider } from '../contexts/DialogContext';
import { doctorApi } from '../api/doctorApi';

// Mock doctorApi
vi.mock('../api/doctorApi', () => ({
    doctorApi: {
        getQueue: vi.fn(),
        checkInAppointment: vi.fn(),
        startConsultation: vi.fn(),
        markNoShow: vi.fn(),
    }
}));

const mockQueueItems = [
    {
        appointmentId: 101,
        appointmentCode: 'APPT-2026-0101',
        patientId: 20,
        patientName: 'Nguyễn Văn Nam',
        patientPhone: '0901234567',
        patientGender: 'Male',
        patientAge: 45,
        reason: 'Khám kiểm tra định kỳ',
        appointmentDate: '2026-09-05',
        startTime: '08:30',
        endTime: '09:00',
        status: 'Confirmed',
        queueOrder: 1,
        isVitalsRecorded: true,
        vitalSummaryText: '120/80 mmHg, 72 bpm',
        chiefComplaint: 'Đau đầu nhẹ'
    },
    {
        appointmentId: 102,
        appointmentCode: 'APPT-2026-0102',
        patientId: 21,
        patientName: 'Trần Thị Thảo',
        patientPhone: '0912345679',
        patientGender: 'Female',
        patientAge: 32,
        reason: 'Tái khám hô hấp',
        appointmentDate: '2026-09-05',
        startTime: '09:00',
        endTime: '09:30',
        status: 'CheckedIn',
        queueOrder: 2,
        isVitalsRecorded: false,
        vitalSummaryText: null,
        chiefComplaint: null
    }
];

describe('DoctorQueue Component', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders queue items and summary tabs correctly', async () => {
        vi.mocked(doctorApi.getQueue).mockResolvedValue({
            success: true,
            message: '',
            data: mockQueueItems
        });

        render(
            <MemoryRouter>
                <DialogProvider>
                    <DoctorQueue />
                </DialogProvider>
            </MemoryRouter>
        );

        // Header
        expect(screen.getByText(/Hàng đợi khám lâm sàng/i)).toBeInTheDocument();

        // Patients in queue
        await waitFor(() => {
            expect(screen.getByText('Nguyễn Văn Nam')).toBeInTheDocument();
            expect(screen.getByText('Trần Thị Thảo')).toBeInTheDocument();
        });

        expect(screen.getByText('APPT-2026-0101')).toBeInTheDocument();
        expect(screen.getByText('APPT-2026-0102')).toBeInTheDocument();

        // Check Action buttons exist
        expect(screen.getByText('Tiếp nhận')).toBeInTheDocument();
        expect(screen.getByText('Vào khám')).toBeInTheDocument();
    });

    it('displays empty state when queue is empty', async () => {
        vi.mocked(doctorApi.getQueue).mockResolvedValue({
            success: true,
            message: '',
            data: []
        });

        render(
            <MemoryRouter>
                <DialogProvider>
                    <DoctorQueue />
                </DialogProvider>
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Không tìm thấy bệnh nhân nào trong hàng đợi')).toBeInTheDocument();
        });
    });

    it('displays inline error when getQueue fails and allows retry', async () => {
        vi.mocked(doctorApi.getQueue).mockRejectedValueOnce(
            new Error('Lỗi kết nối máy chủ 500')
        );

        render(
            <MemoryRouter>
                <DialogProvider>
                    <DoctorQueue />
                </DialogProvider>
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Lỗi kết nối máy chủ 500')).toBeInTheDocument();
        });

        // Test retry
        vi.mocked(doctorApi.getQueue).mockResolvedValueOnce({
            success: true,
            message: '',
            data: mockQueueItems
        });

        const retryButton = screen.getByText('Thử lại');
        fireEvent.click(retryButton);

        await waitFor(() => {
            expect(screen.getByText('Nguyễn Văn Nam')).toBeInTheDocument();
        });
    });
});

