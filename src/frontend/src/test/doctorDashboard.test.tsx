import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import { DialogProvider } from '../contexts/DialogContext';
import { doctorApi } from '../api/doctorApi';

// Mock doctorApi
vi.mock('../api/doctorApi', () => ({
    doctorApi: {
        getDashboard: vi.fn(),
        checkInAppointment: vi.fn(),
        startConsultation: vi.fn(),
    }
}));

const mockDashboardData = {
    currentShift: {
        id: 101,
        date: '2026-09-05',
        shiftName: 'Ca sáng',
        startTime: '08:00',
        endTime: '12:00',
        room: 'Phòng 204',
        status: 'InProgress'
    },
    kpis: {
        totalAppointmentsToday: 12,
        waitingCount: 5,
        inConsultationCount: 1,
        completedCount: 6,
        noShowCount: 0
    },
    nextPatient: {
        appointmentId: 55,
        appointmentCode: 'APPT-2026-0055',
        patientId: 10,
        patientName: 'Trần Thị Mai',
        patientPhone: '0912345678',
        patientGender: 'Female',
        patientDob: '1985-04-12',
        patientAge: 41,
        reason: 'Đau thắt ngực khi gắng sức',
        appointmentDate: '2026-09-05',
        startTime: '09:30',
        endTime: '10:00',
        status: 'CheckedIn',
        queueOrder: 1,
        isVitalsRecorded: true,
        vitalSummaryText: 'Huyết áp 120/80 mmHg, Mạch 75 bpm'
    },
    todayQueue: [
        {
            appointmentId: 55,
            appointmentCode: 'APPT-2026-0055',
            patientId: 10,
            patientName: 'Trần Thị Mai',
            patientPhone: '0912345678',
            patientGender: 'Female',
            patientDob: '1985-04-12',
            patientAge: 41,
            reason: 'Đau thắt ngực khi gắng sức',
            appointmentDate: '2026-09-05',
            startTime: '09:30',
            endTime: '10:00',
            status: 'CheckedIn',
            queueOrder: 1,
            isVitalsRecorded: true
        },
        {
            appointmentId: 56,
            appointmentCode: 'APPT-2026-0056',
            patientId: 11,
            patientName: 'Lê Văn An',
            patientPhone: '0987654321',
            patientGender: 'Male',
            patientDob: '1990-08-20',
            patientAge: 36,
            reason: 'Kiểm tra huyết áp',
            appointmentDate: '2026-09-05',
            startTime: '10:00',
            endTime: '10:30',
            status: 'Confirmed',
            queueOrder: 2,
            isVitalsRecorded: false
        }
    ]
};

describe('DoctorDashboard Component', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders current active shift, and KPI cards correctly', async () => {
        vi.mocked(doctorApi.getDashboard).mockResolvedValue({
            success: true,
            message: '',
            data: mockDashboardData as any
        });

        render(
            <MemoryRouter>
                <DialogProvider>
                    <DoctorDashboard />
                </DialogProvider>
            </MemoryRouter>
        );

        // Expect shift details
        await waitFor(() => {
            expect(screen.getByText(/Ca sáng/i)).toBeInTheDocument();
            expect(screen.getByText(/Phòng 204/i)).toBeInTheDocument();
        });

        // KPIs
        expect(screen.getByText('12')).toBeInTheDocument(); // Tổng số ca hôm nay
        expect(screen.getByText('6')).toBeInTheDocument();  // Đã hoàn tất

        // Next patient banner and queue table
        expect(screen.getAllByText(/Trần Thị Mai/i).length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText(/Đau thắt ngực khi gắng sức/i).length).toBeGreaterThanOrEqual(1);

        // Queue table rows
        expect(screen.getByText('APPT-2026-0055')).toBeInTheDocument();
        expect(screen.getByText(/Lê Văn An/i)).toBeInTheDocument();
    });

    it('renders empty queue message when doctor has no appointments today', async () => {
        vi.mocked(doctorApi.getDashboard).mockResolvedValue({
            success: true,
            message: '',
            data: {
                currentShift: null,
                kpis: {
                    totalAppointmentsToday: 0,
                    waitingCount: 0,
                    inConsultationCount: 0,
                    completedCount: 0,
                    noShowCount: 0
                },
                nextPatient: null,
                todayQueue: []
            } as any
        });

        render(
            <MemoryRouter>
                <DialogProvider>
                    <DoctorDashboard />
                </DialogProvider>
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText(/Không có lịch khám nào được ghi nhận hôm nay/i)).toBeInTheDocument();
        });
    });
});
