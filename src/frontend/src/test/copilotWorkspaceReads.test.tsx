import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';

describe('Copilot workspace read cards', () => {
    it('shows at most five masked patient matches and only the specified patient fields', () => {
        const view = render(<>{renderCopilotCardData({ type: 'reception_patient_lookup', title: 'Bệnh nhân', data: Array.from({ length: 6 }, (_, i) => ({
            fullName: `Người bệnh ${i}`, medicalRecordNumber: `MRN-${i}`, birthYear: 1987, phone: '0983***004', nationalId: 'SECRET-ID',
            upcomingAppointment: { appointmentCode: `APT-${i}`, appointmentDate: '2030-01-02', startTime: '10:30:00' }
        })) })}</>);
        expect(screen.getByText('Người bệnh 0')).toBeInTheDocument();
        expect(screen.queryByText('Người bệnh 5')).not.toBeInTheDocument();
        expect(screen.getAllByText('0983***004')).toHaveLength(5);
        expect(view.container.textContent).toContain('02/01/2030 10:30');
        expect(view.container.textContent).not.toContain('SECRET-ID');
    });

    it('shows exactly one next patient, with visit identity, time and reason', () => {
        const view = render(<>{renderCopilotCardData({ type: 'doctor_next_patient', title: 'Tiếp theo', data: { items: [
            { patientName: 'Người tiếp theo', visitCode: 'V-1', startTime: '10:30', reason: 'Đau đầu', patientId: 'INTERNAL-ID' },
            { patientName: 'Không phải người tiếp theo', visitCode: 'V-2' }
        ] } })}</>);
        expect(screen.getByText('Người tiếp theo')).toBeInTheDocument();
        expect(screen.getByText('V-1')).toBeInTheDocument();
        expect(screen.getByText('10:30')).toBeInTheDocument();
        expect(screen.getByText('Đau đầu')).toBeInTheDocument();
        expect(screen.queryByText('Không phải người tiếp theo')).not.toBeInTheDocument();
        expect(view.container.textContent).not.toContain('INTERNAL-ID');
    });
});
