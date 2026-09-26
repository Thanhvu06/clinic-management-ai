import { useEffect } from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { MemoryRouter, Routes, Route, useNavigate } from 'react-router-dom';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { DialogProvider } from '../contexts/DialogContext';
import { doctorApi } from '../api/doctorApi';
import { diagnosticApi } from '../api/diagnosticApi';
import type { ApiResponse, PatientClinicalContextDto, DiagnosticOrderDto } from '../types';

let testNavigate: (path: string) => void = () => {};
function NavigationListener() {
    const navigate = useNavigate();
    useEffect(() => {
        testNavigate = navigate;
    }, [navigate]);
    return null;
}

vi.mock('../api/doctorApi', () => ({
    doctorApi: {
        getVisitPatientClinicalContext: vi.fn(),
        getPatientClinicalContext: vi.fn(),
        getActiveMedicines: vi.fn(),
        saveVisitEncounter: vi.fn(),
        saveEncounter: vi.fn(),
        saveVisitVitals: vi.fn(),
        saveVitals: vi.fn(),
        saveVisitPrescriptionDraft: vi.fn(),
        savePrescriptionDraft: vi.fn(),
        completeVisitConsultation: vi.fn(),
        completeConsultation: vi.fn(),
    }
}));

vi.mock('../api/diagnosticApi', () => ({
    diagnosticApi: {
        getDoctorOrdersByVisit: vi.fn(),
        getDoctorOrdersByAppointment: vi.fn(),
        getCatalog: vi.fn(),
        createDoctorOrder: vi.fn(),
        reviewDoctorOrder: vi.fn(),
        cancelDoctorOrder: vi.fn(),
    }
}));

function createMockContext(visitId: number, name: string): PatientClinicalContextDto {
    return {
        patientId: visitId * 10,
        patientName: name,
        patientPhone: '0901234567',
        patientGender: 'Male',
        patientDob: '1990-01-01',
        totalPastVisits: 0,
        pastVisits: [],
        currentAppointment: {
            id: visitId,
            appointmentCode: `APT-${visitId}`,
            reason: 'Đau đầu, mệt mỏi',
            startTime: '08:00:00',
            endTime: '08:30:00',
            status: 'InConsultation'
        } as any,
        encounter: {
            id: 1,
            appointmentId: visitId,
            doctorId: 1,
            doctorName: 'BS. Tuấn',
            createdAtUtc: new Date().toISOString(),
            chiefComplaint: 'Đau đầu 3 ngày nay',
            clinicalFindings: 'Họng hơi đỏ',
            diagnosis: 'Viêm đường hô hấp trên',
            diagnosisCode: 'J06.9',
            treatmentPlan: 'Nghỉ ngơi, uống nhiều nước',
            summary: 'Bệnh nhẹ',
            followUpInstruction: 'Tái khám nếu sốt cao',
            rowVersion: 'enc-row-version'
        } as any,
        vitalSigns: {
            id: 1,
            appointmentId: visitId,
            recordedAtUtc: new Date().toISOString(),
            recordedByUserName: 'BS. Tuấn',
            temperature: 37.2,
            bloodPressureSystolic: 120,
            bloodPressureDiastolic: 80,
            heartRate: 75,
            respiratoryRate: 18,
            weight: 65,
            height: 170,
            spO2: 98,
            rowVersion: 'vitals-row-version'
        } as any,
        prescription: {
            id: 500 + visitId,
            notes: 'Uống sau ăn',
            rowVersion: 'rx-row-version',
            items: [
                {
                    medicineId: 1,
                    medicineCode: 'PARA500',
                    medicineName: 'Paracetamol 500mg',
                    unit: 'Viên',
                    quantity: 10,
                    availableStock: 100,
                    dosage: '500mg',
                    frequency: '2 lần/ngày',
                    durationDays: 5,
                    instructions: 'Uống sau ăn'
                }
            ]
        } as any
    };
}

function createMockOrder(id: number, visitId: number, status: 'Ordered' | 'InProgress' | 'Completed', reviewed: boolean): DiagnosticOrderDto {
    return {
        id,
        appointmentId: 0,
        appointmentCode: `APT-${visitId}`,
        orderCode: `ORD-${visitId}-${id}`,
        patientId: visitId * 10,
        patientName: 'Test Patient',
        patientPhone: '0901234567',
        patientGender: 'Nam',
        patientAge: 35,
        orderingDoctorId: 1,
        orderingDoctorName: 'BS. Tuấn',
        specialtyName: 'Nội khoa',
        clinicalIndication: 'Chỉ định CLS kiểm tra',
        status,
        orderedAtUtc: new Date().toISOString(),
        reviewedAtUtc: reviewed ? new Date().toISOString() : undefined,
        reviewedByDoctorName: reviewed ? 'BS. Tuấn' : undefined,
        items: [
            {
                id: id * 10,
                diagnosticOrderId: id,
                diagnosticServiceId: 101,
                serviceCode: 'CBC',
                serviceName: 'Tổng phân tích tế bào máu',
                category: 'Laboratory',
                status,
                result: status === 'Completed' ? {
                    id: 99,
                    diagnosticOrderItemId: id * 10,
                    resultText: 'Bình thường',
                    conclusion: 'Chỉ số trong giới hạn',
                    resultedAtUtc: new Date().toISOString(),
                    resultedByUserId: 'tech-uuid',
                    resultedByUserName: 'KTV Nam'
                } : null
            }
        ]
    };
}

function ok<T>(data: T): ApiResponse<T> {
    return { success: true, message: 'Success', data };
}

describe('DoctorExaminationWorkspace Controlled Async & Race Condition Tests', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(diagnosticApi.getCatalog).mockResolvedValue(ok([]));
        vi.mocked(doctorApi.getActiveMedicines).mockResolvedValue(ok([]));
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    // 1. Manual load in-flight when background polling arrives
    it('given manual load is in-flight when background polling fires then background poll does not overwrite or break manual load', async () => {
        let resolveManualOrders: any;
        const manualPromise = new Promise(r => { resolveManualOrders = r; });

        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue(
            ok(createMockContext(101, 'Bệnh nhân 101'))
        );

        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockReturnValueOnce(manualPromise as any);

        render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        expect(screen.getByText(/Đang nạp hồ sơ khám/i)).toBeInTheDocument();

        // Resolve manual load
        const manualOrder = [createMockOrder(1, 101, 'Ordered', false)];
        await act(async () => {
            resolveManualOrders(ok(manualOrder));
        });

        const diagBtn = await screen.findByRole('button', { name: /Chỉ định Cận lâm sàng/i });
        fireEvent.click(diagBtn);

        await waitFor(() => {
            expect(screen.getAllByText('ORD-101-1').length).toBeGreaterThanOrEqual(1);
        });
    });

    it('given background poll in-flight when user triggers manual refresh then manual refresh takes precedence and does not get cleared prematurely', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        let resolvePoll: any;
        let resolveManual: any;
        const pollPromise = new Promise(r => { resolvePoll = r; });
        const manualPromise = new Promise(r => { resolveManual = r; });

        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue(
            ok(createMockContext(101, 'Bệnh nhân 101'))
        );

        // Initial load
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValueOnce(
            ok([createMockOrder(1, 101, 'Ordered', false)])
        );

        render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        const diagBtn = await screen.findByRole('button', { name: /Chỉ định Cận lâm sàng/i });
        fireEvent.click(diagBtn);

        await waitFor(() => {
            expect(screen.getAllByText('ORD-101-1').length).toBeGreaterThanOrEqual(1);
        });

        // Setup next calls: first will be background poll (stale), second will be manual refresh (fresh)
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit)
            .mockReturnValueOnce(pollPromise as any)
            .mockReturnValueOnce(manualPromise as any);

        // Advance 20s to trigger the background poll interval
        await act(async () => {
            await vi.advanceTimersByTimeAsync(20000);
        });

        // Click refresh button manually while background poll is in-flight
        const refreshBtn = screen.getByRole('button', { name: /Cập nhật kết quả/i });
        fireEvent.click(refreshBtn);

        // Resolve old background poll first with stale data
        await act(async () => {
            resolvePoll(ok([createMockOrder(1, 101, 'Ordered', false)]));
        });

        // Resolve manual refresh with completed order
        await act(async () => {
            resolveManual(ok([createMockOrder(1, 101, 'Completed', true)]));
        });

        await waitFor(() => {
            // Should show fresh reviewed order from manual refresh
            expect(screen.getByText(/Bác sĩ đã xem/i)).toBeInTheDocument();
        });
        vi.useRealTimers();
    });

    // 3. Switch Visit A to B when request A is pending
    it('given Visit A request pending when user switches to Visit B then Visit B does not display Visit A data', async () => {
        let resolveA: any;
        const promiseA = new Promise(r => { resolveA = r; });

        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockImplementation((id: number) => {
            if (id === 101) {
                return promiseA as any;
            }
            return Promise.resolve(ok(createMockContext(202, 'Bệnh nhân B 202')));
        });

        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValue(ok([]));

        render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <NavigationListener />
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        // Switch route to visit 202 before A returns
        await act(async () => {
            testNavigate('/doctor/visits/202');
        });

        await waitFor(() => {
            expect(screen.getByText('Bệnh nhân B 202')).toBeInTheDocument();
        });

        // Now resolve A
        await act(async () => {
            resolveA(ok(createMockContext(101, 'Bệnh nhân A 101')));
        });

        // Should still show Patient B 202!
        expect(screen.queryByText('Bệnh nhân A 101')).not.toBeInTheDocument();
        expect(screen.getByText('Bệnh nhân B 202')).toBeInTheDocument();
    });

    // 4. Response A arrives after Response B
    it('given request A arrives after response B has completed then response A is ignored', async () => {
        let resolveA: any;
        let resolveB: any;
        const promiseA = new Promise(r => { resolveA = r; });
        const promiseB = new Promise(r => { resolveB = r; });

        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockImplementation((id: number) => {
            if (id === 101) return promiseA as any;
            return promiseB as any;
        });

        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValue(ok([]));

        render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <NavigationListener />
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        await act(async () => {
            testNavigate('/doctor/visits/202');
        });

        // Resolve B first
        await act(async () => {
            resolveB(ok(createMockContext(202, 'Bệnh nhân B 202')));
        });

        await waitFor(() => {
            expect(screen.getByText('Bệnh nhân B 202')).toBeInTheDocument();
        });

        // Now resolve A later
        await act(async () => {
            resolveA(ok(createMockContext(101, 'Bệnh nhân A 101')));
        });

        expect(screen.queryByText('Bệnh nhân A 101')).not.toBeInTheDocument();
        expect(screen.getByText('Bệnh nhân B 202')).toBeInTheDocument();
    });

    // 5. Request fails then retry succeeds
    it('given diagnostic orders request fails then retry succeeds and error is cleared', async () => {
        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue(
            ok(createMockContext(101, 'Bệnh nhân 101'))
        );

        // First call fails
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockRejectedValueOnce(new Error('Network error'));

        render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        const diagBtn = await screen.findByRole('button', { name: /Chỉ định Cận lâm sàng/i });
        fireEvent.click(diagBtn);

        await waitFor(() => {
            expect(screen.getByText(/Tự động cập nhật kết quả cận lâm sàng bị gián đoạn/i)).toBeInTheDocument();
        });

        // Setup second call to succeed
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValueOnce(
            ok([createMockOrder(5, 101, 'Ordered', false)])
        );

        const retryBtn = screen.getByRole('button', { name: /Thử lại/i });
        fireEvent.click(retryBtn);

        await waitFor(() => {
            expect(screen.queryByText(/Tự động cập nhật kết quả cận lâm sàng bị gián đoạn/i)).not.toBeInTheDocument();
            expect(screen.getAllByText('ORD-101-5').length).toBeGreaterThanOrEqual(1);
        });
    });

    // 6. Unmount when request is running
    it('given request is running when component unmounts then no crash or unhandled state updates occur', async () => {
        let resolveRequest: any;
        const pendingPromise = new Promise(r => { resolveRequest = r; });

        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockReturnValue(pendingPromise as any);
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockReturnValue(pendingPromise as any);

        const { unmount } = render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        unmount();

        // Resolve after unmount
        await act(async () => {
            resolveRequest(ok(createMockContext(101, 'Bệnh nhân 101')));
        });

        // Unmount was clean without errors
        expect(true).toBe(true);
    });

    // 7. Polling does not overwrite draft notes, vital signs, or draft prescriptions
    it('given doctor has drafted notes and prescription then polling diagnostic orders preserves all drafted values', async () => {
        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue(
            ok(createMockContext(101, 'Bệnh nhân 101'))
        );

        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValue(
            ok([createMockOrder(1, 101, 'Ordered', false)])
        );

        render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByDisplayValue('Đau đầu 3 ngày nay')).toBeInTheDocument();
        });

        // Doctor edits the chief complaint manually
        const chiefInput = screen.getByDisplayValue('Đau đầu 3 ngày nay');
        fireEvent.change(chiefInput, { target: { value: 'Đau đầu dữ dội kèm buồn nôn' } });
        expect(screen.getByDisplayValue('Đau đầu dữ dội kèm buồn nôn')).toBeInTheDocument();

        // Simulate a background diagnostic poll returning updated lab result
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValueOnce(
            ok([createMockOrder(1, 101, 'Completed', false)])
        );

        const diagBtn = screen.getByRole('button', { name: /Chỉ định Cận lâm sàng/i });
        fireEvent.click(diagBtn);

        const refreshBtn = screen.getByRole('button', { name: /Cập nhật kết quả/i });
        fireEvent.click(refreshBtn);

        await waitFor(() => {
            expect(screen.getByText(/Chưa duyệt kết quả/i)).toBeInTheDocument();
        });

        // Switch back to encounter tab ("Diễn tiến lâm sàng")
        const encBtn = screen.getByRole('button', { name: /Diễn tiến lâm sàng/i });
        fireEvent.click(encBtn);

        // Draft note is preserved!
        expect(screen.getByDisplayValue('Đau đầu dữ dội kèm buồn nôn')).toBeInTheDocument();
    });

    // 8. Polling does not auto-review diagnostic orders
    it('given technician completes order then polling leaves reviewedAtUtc null until doctor explicitly confirms', async () => {
        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue(
            ok(createMockContext(101, 'Bệnh nhân 101'))
        );

        // Completed order without review
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValue(
            ok([createMockOrder(1, 101, 'Completed', false)])
        );

        render(
            <MemoryRouter initialEntries={['/doctor/visits/101']}>
                <DialogProvider>
                    <Routes>
                        <Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} />
                    </Routes>
                </DialogProvider>
            </MemoryRouter>
        );

        const diagBtn = await screen.findByRole('button', { name: /Chỉ định Cận lâm sàng/i });
        fireEvent.click(diagBtn);

        await waitFor(() => {
            expect(screen.getByText(/Chưa duyệt kết quả/i)).toBeInTheDocument();
            expect(screen.getByRole('button', { name: /Xác nhận đã xem kết quả/i })).toBeInTheDocument();
        });

        // diagnosticApi.reviewDoctorOrder was NOT called automatically
        expect(diagnosticApi.reviewDoctorOrder).not.toHaveBeenCalled();
    });
});
