import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { CheckInTicketModal } from '../components/CheckInTicketModal';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import { AdminAuditLogs } from '../pages/admin/AdminAuditLogs';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import type { CheckInTicketDto } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() }) }));

it('shows the short appointment code in the patient list and assistant card', async () => {
    const appointment = {
        id: 23, appointmentCode: 'LH1025', status: 'Confirmed', appointmentDate: '2030-01-02',
        patientName: 'Người bệnh', doctorName: 'Bác sĩ', startTime: '09:00:00', endTime: '09:30:00',
    };
    vi.mocked(axiosClient.get).mockImplementation(async url => ({
        success: true, message: '', data: url.startsWith('/appointments/my?') ? { items: [appointment] } : [],
    }));
    const page = render(<MemoryRouter><PatientAppointments /></MemoryRouter>);
    expect(await screen.findByText('LH1025')).toBeVisible();
    page.unmount();

    const { container } = render(<>{renderCopilotCardData({
        type: 'reception_appointments', title: 'Lịch hẹn', data: [appointment],
    })}</>);
    expect(container).toHaveTextContent('LH1025');
});

it('keeps the department queue number visible next to the short visit and record codes on the ticket', () => {
    const ticket: CheckInTicketDto = {
        visitId: 1, visitCode: 'LK0042', patientId: 1, patientName: 'Người bệnh', medicalRecordNumber: 'BN0102',
        phoneNumber: '0901234567', queueNumber: 1, queueDisplay: 'KCXK-001', facilityId: 1, facilityName: 'Phòng khám',
        departmentId: 1, departmentName: 'Cơ xương khớp', checkedInAtUtc: '2026-10-22T01:00:00Z',
        receptionistName: 'Lễ tân', status: 'WaitingForDoctor', priority: 'Normal', arrivalType: 'WalkIn',
    };
    render(<CheckInTicketModal isOpen ticket={ticket} onClose={vi.fn()} />);
    for (const value of ['KCXK', '001', 'LK0042', 'BN0102'])
        expect(screen.getByText(value)).toBeVisible();
});

it('submits audit searches in either format and displays the short reference', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({
        success: true, message: '', data: { items: [{
            id: 1, action: 'CreateAppointment', entityName: 'Appointment', entityId: '1', referenceCode: 'LH1025',
            description: 'Đặt lịch hẹn', userFullName: 'Bệnh nhân', createdAt: '2026-11-28T01:00:00Z',
        }], totalItems: 1 },
    });
    render(<AdminAuditLogs />);
    expect(await screen.findByText('LH1025')).toBeVisible();
    for (const code of ['lh 1025', 'APT-261128-ABC12345']) {
        fireEvent.change(screen.getByRole('searchbox', { name: 'Mã tham chiếu' }), { target: { value: code } });
        fireEvent.click(screen.getByRole('button', { name: 'Tìm' }));
        await waitFor(() => expect(axiosClient.get).toHaveBeenLastCalledWith(
            '/admin/audit-logs?' + new URLSearchParams({ page: '1', pageSize: '15', code })));
    }
});
