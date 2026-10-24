import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { TechnicianDashboard } from '../pages/technician/TechnicianDashboard';
import type { DiagnosticOrderDto } from '../types';

const mocks = vi.hoisted(() => ({ orders: vi.fn(), stats: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrders: mocks.orders, getTechnicianStats: mocks.stats } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn(), showConfirm: vi.fn() }) }));

const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
const completed: DiagnosticOrderDto = {
    id: 7, appointmentId: 0, appointmentCode: '', orderCode: 'CLS-261024-D0005', patientId: 2,
    patientName: 'Trần Văn Bình', patientPhone: '0983000002', patientGender: 'Male', patientAge: 47,
    orderingDoctorId: 4, orderingDoctorName: 'BS. Phạm Văn Hùng', specialtyName: 'Tai mũi họng', clinicalIndication: 'Ho kéo dài',
    status: 'Completed', orderedAtUtc: '2026-10-24T01:50:00Z', rowVersion: 'v1',
    items: [{ id: 70, diagnosticOrderId: 7, diagnosticServiceId: 9, serviceCode: 'IMG-CXR', serviceName: 'Chụp X-quang ngực thẳng', category: 'Imaging', status: 'Completed', result: null }],
};

afterEach(cleanup);

it('shows the completed order time as HH:mm dd/MM in Vietnam time', async () => {
    mocks.orders.mockResolvedValue(ok({ items: [completed], totalItems: 1, page: 1, pageSize: 15 }));
    mocks.stats.mockResolvedValue(ok({ orderedCount: 0, inProgressCount: 0, completedTodayCount: 1 }));
    render(<MemoryRouter><TechnicianDashboard /></MemoryRouter>);

    expect(await screen.findByText('08:50 24/10')).toBeInTheDocument();
    expect(screen.queryByText(/24-10/)).not.toBeInTheDocument();
});
