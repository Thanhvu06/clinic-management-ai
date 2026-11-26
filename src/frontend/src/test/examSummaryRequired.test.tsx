import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { DialogProvider } from '../contexts/DialogContext';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { doctorApi } from '../api/doctorApi';
import { diagnosticApi } from '../api/diagnosticApi';
import { completionFieldMissingMessage, isSummaryMissing, LEGACY_SUMMARY_PLACEHOLDER, loadedSummaryValue } from '../pages/doctor/examinationSummary';
import type { PatientClinicalContextDto } from '../types';

vi.mock('../api/doctorApi', () => ({
    doctorApi: {
        getVisitPatientClinicalContext: vi.fn(),
        getActiveMedicines: vi.fn(),
        completeVisitConsultation: vi.fn(),
    }
}));
vi.mock('../api/diagnosticApi', () => ({
    diagnosticApi: { getDoctorOrdersByVisit: vi.fn(), getCatalog: vi.fn() }
}));

const ok = <T,>(data: T) => ({ success: true, message: '', data });

const context = (summary: string): PatientClinicalContextDto => ({
    patientId: 70, patientName: 'Người bệnh kết luận', patientPhone: '0901234567', patientGender: 'Female', patientDob: '1990-03-04',
    totalPastVisits: 0, pastVisits: [],
    currentAppointment: { id: 7, appointmentCode: 'APT-7', reason: 'Đau đầu', startTime: '08:00:00', endTime: '08:30:00', status: 'InConsultation' },
    encounter: { id: 1, appointmentId: 7, doctorId: 1, doctorName: 'BS. Kiểm thử', createdAtUtc: new Date().toISOString(),
        chiefComplaint: 'Đau đầu', diagnosis: 'Đau đầu do căng thẳng', summary, rowVersion: 'row' },
} as unknown as PatientClinicalContextDto);

const mount = () => render(
    <MemoryRouter initialEntries={['/doctor/visits/7/examination']}>
        <DialogProvider>
            <CopilotResourceProvider>
                <Routes><Route path="/doctor/visits/:visitId/examination" element={<DoctorExaminationWorkspace />} /></Routes>
            </CopilotResourceProvider>
        </DialogProvider>
    </MemoryRouter>
);

describe('examination summary helpers', () => {
    it('treats empty and the legacy pre-filled sentence as a missing conclusion', () => {
        expect(isSummaryMissing('')).toBe(true);
        expect(isSummaryMissing('   ')).toBe(true);
        expect(isSummaryMissing(null)).toBe(true);
        expect(isSummaryMissing(LEGACY_SUMMARY_PLACEHOLDER)).toBe(true);
        expect(isSummaryMissing(`  ${LEGACY_SUMMARY_PLACEHOLDER}  `)).toBe(true);
        expect(isSummaryMissing('Bệnh nhân ổn định, điều trị ngoại trú')).toBe(false);
    });

    it('never loads the legacy sentence into the field as a real value', () => {
        expect(loadedSummaryValue(LEGACY_SUMMARY_PLACEHOLDER)).toBe('');
        expect(loadedSummaryValue(undefined)).toBe('');
        expect(loadedSummaryValue('Kết luận thật')).toBe('Kết luận thật');
    });

    it('reports the diagnosis first, then the conclusion', () => {
        expect(completionFieldMissingMessage('', 'Kết luận')).toMatch(/chẩn đoán/);
        expect(completionFieldMissingMessage('Chẩn đoán', LEGACY_SUMMARY_PLACEHOLDER)).toMatch(/tóm tắt kết luận/);
        expect(completionFieldMissingMessage('Chẩn đoán', 'Kết luận')).toBeNull();
    });
});

describe('completing an examination whose stored draft still holds the legacy sentence', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(diagnosticApi.getCatalog).mockResolvedValue(ok([]) as never);
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValue(ok([]) as never);
        vi.mocked(doctorApi.getActiveMedicines).mockResolvedValue(ok([]) as never);
    });

    it('shows an empty conclusion with only the placeholder hint and blocks completion', async () => {
        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue(ok(context(LEGACY_SUMMARY_PLACEHOLDER)) as never);
        mount();
        await screen.findByText('Người bệnh kết luận');

        const field = screen.getByPlaceholderText('Tóm tắt chẩn đoán và tình trạng chung của bệnh nhân...') as HTMLTextAreaElement;
        expect(field.value).toBe('');
        expect(screen.queryByDisplayValue(LEGACY_SUMMARY_PLACEHOLDER)).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /Hoàn tất khám/ }));
        expect(await screen.findByText('Vui lòng nhập tóm tắt kết luận khám trước khi hoàn tất.')).toBeInTheDocument();
        expect(screen.queryByText(/Tóm tắt:/)).not.toBeInTheDocument();
        expect(doctorApi.completeVisitConsultation).not.toHaveBeenCalled();
    }, 15_000);

    it('still opens the confirmation once a real conclusion is typed', async () => {
        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue(ok(context(LEGACY_SUMMARY_PLACEHOLDER)) as never);
        mount();
        await screen.findByText('Người bệnh kết luận');

        fireEvent.change(screen.getByPlaceholderText('Tóm tắt chẩn đoán và tình trạng chung của bệnh nhân...'), { target: { value: 'Đau đầu căng thẳng, điều trị ngoại trú' } });
        fireEvent.click(screen.getByRole('button', { name: /Hoàn tất khám/ }));
        await waitFor(() => expect(screen.getByText(/Đau đầu căng thẳng, điều trị ngoại trú/, { selector: 'p, div, span' })).toBeInTheDocument());
        expect(screen.queryByText('Vui lòng nhập tóm tắt kết luận khám trước khi hoàn tất.')).not.toBeInTheDocument();
    }, 15_000);
});
