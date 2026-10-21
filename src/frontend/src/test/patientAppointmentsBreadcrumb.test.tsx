import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { PatientAppointments } from '../pages/patient/PatientAppointments';

vi.mock('../api/axiosClient', () => ({
    default: { get: vi.fn().mockResolvedValue({ success: true, data: { items: [] } }) }
}));
vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() })
}));

it('uses Lịch hẹn của tôi in the patient appointment breadcrumb', async () => {
    render(<MemoryRouter><PatientAppointments /></MemoryRouter>);

    await screen.findByText('Bạn chưa có lịch hẹn nào');
    const breadcrumb = screen.getByRole('navigation');
    expect(within(breadcrumb).getByText('Lịch hẹn của tôi')).toBeInTheDocument();
    expect(within(breadcrumb).queryByText('Lịch sử khám')).not.toBeInTheDocument();
});
