import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AiCopilotCard } from '../api/aiCopilotApi';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import { CANONICAL_COPILOT_CARD_TYPES, isCanonicalCopilotCardType } from '../components/copilot/copilotCardContract';

const fixtures: Record<string, unknown> = {
    clinic_knowledge: { status: 'matched', items: [{ title: 'Tim mạch', details: { specialtyCode: 'SP06' }, sourceId: 'internal:1' }] },
    specialties: [{ id: 1, code: 'SP06', name: 'Tim mạch', description: 'Khám chuyên khoa' }],
    doctors: [{ id: 2, name: 'Bác sĩ A', academicTitle: 'BS', specialtyName: 'Tim mạch' }],
    available_slots: [{ slotId: 3, slotDate: '2030-01-02', startTime: '08:00', endTime: '08:30' }],
    facilities: [{ id: 4, name: 'Cơ sở X', address: 'Địa chỉ công khai', phone: '0900000000' }],
    pricing_catalog: { consultation: [{ specialtyId: 1, specialty: 'Tim mạch', consultationFee: 250000 }], diagnostics: [{ serviceId: 2, name: 'Siêu âm', price: 180000 }] },
    appointments: { items: [{ id: 5, appointmentCode: 'AP-5', status: 'Confirmed', appointmentDate: '2030-01-02', doctorName: 'BS A' }] },
    appointment_detail: { id: 5, appointmentCode: 'AP-5', status: 'Confirmed', appointmentDate: '2030-01-02' },
    patient_visits: [{ id: 6, visitCode: 'V-6', visitDate: '2030-01-02', status: 'Completed' }],
    patient_diagnostic_results: [{ orderCode: 'LAB-1', publishedToPatient: true, items: [{ service: 'Siêu âm', result: { ResultText: 'Bình thường' } }] }],
    patient_prescriptions: [{ id: 7, status: 'Issued', items: [{ medicine: 'Paracetamol', Dosage: '500mg' }] }],
    patient_bills: [{ id: 8, invoiceCode: 'INV-8', status: 'Paid', totalAmount: 100000, items: [{ Description: 'Khám', Quantity: 1, LineTotal: 100000 }] }],
    reception_appointments: [{ id: 9, appointmentCode: 'AP-9', patientName: 'Người bệnh', status: 'Confirmed', appointmentDate: '2030-01-02' }],
    reception_queue: [{ id: 10, visitCode: 'V-10', queueNumber: 1, patientName: 'Người bệnh', status: 'Waiting' }],
    appointment_lookup: { id: 11, appointmentCode: 'AP-11', status: 'Confirmed', appointmentDate: '2030-01-02' },
    doctor_queue: [{ id: 12, visitCode: 'V-12', queueNumber: 2, patientName: 'Người bệnh', status: 'Waiting' }],
    doctor_summary: [{ id: 13, visitCode: 'V-13', patientName: 'Người bệnh', status: 'InProgress' }],
    doctor_patient_summary: { id: 13, visitCode: 'V-13', patientName: 'Người bệnh', status: 'InProgress' },
    doctor_diagnostic_orders: [{ id: 14, orderCode: 'LAB-14', status: 'Ordered', items: [{ service: 'Xét nghiệm', status: 'Ordered' }] }],
    doctor_prescription_status: [{ id: 15, status: 'Issued', items: [{ medicine: 'Thuốc A', Dosage: '1 viên' }] }],
    technician_worklist: [{ id: 16, orderCode: 'LAB-16', status: 'Ordered', items: [{ service: 'Xét nghiệm', status: 'Ordered' }] }],
    pharmacist_prescription_queue: [{ id: 17, status: 'Issued', paymentStatus: 'unpaid', paymentItems: [{ medicine: 'Thuốc A', requiredQuantity: 1, paidQuantity: 0 }] }],
    pharmacy_inventory: [{ id: 18, name: 'Thuốc A', unit: 'viên', stockQuantity: 10, reorderLevel: 2 }],
    admin_dashboard_metrics: { appointmentsToday: 1, activeVisits: 2, openDiagnosticOrders: 3, issuedPrescriptions: 4 },
    admin_ai_health: { pendingActions: 1, auditEvents: 5 },
    booking_preview: { operation: 'booking', slotDate: '2030-01-02', startTime: '08:00', endTime: '08:30', confirmationToken: 'SECRET_TOKEN' },
    pending_action: { actionId: 'internal-action', appointmentCode: 'AP-20', confirmationToken: 'SECRET_TOKEN', expiresAtUtc: '2030-01-02T08:00:00Z' },
    change_request: { actionId: 'internal-action', changeRequestId: 21, operation: 'cancel' },
    idempotent_replay: { actionId: 'internal-action', reference: 'internal-reference', status: 'already_completed' }
};

describe('canonical Copilot card contract', () => {
    it('has one typed renderer fixture for every canonical result type', () => {
        for (const type of CANONICAL_COPILOT_CARD_TYPES) {
            expect(fixtures[type], `missing fixture for ${type}`).toBeDefined();
            expect(isCanonicalCopilotCardType(type)).toBe(true);
            const card: AiCopilotCard = { type, title: type, data: fixtures[type] };
            const view = render(<>{renderCopilotCardData(card)}</>);
            expect(view.container.textContent).not.toContain('không hỗ trợ trình bày đầy đủ');
            view.unmount();
        }
    });

    it('redacts internal identifiers, contact data, and action secrets from the DOM', () => {
        const card: AiCopilotCard = {
            type: 'patient_diagnostic_results',
            title: 'Kết quả',
            data: [{ orderCode: 'LAB-RED', publishedToPatient: false, patientPhone: '0900000000', items: [{ service: 'Xét nghiệm', result: { ResultText: 'UNPUBLISHED_SECRET' } }] }]
        };
        const view = render(<>{renderCopilotCardData(card)}</>);
        expect(view.container.textContent).toContain('Chưa công bố');
        expect(view.container.textContent).not.toContain('UNPUBLISHED_SECRET');
        expect(view.container.textContent).not.toContain('0900000000');
        expect(view.container.textContent).not.toContain('confirmationToken');
    });
});
