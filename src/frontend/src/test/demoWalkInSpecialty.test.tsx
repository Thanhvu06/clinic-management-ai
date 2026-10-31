import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';

const mocks = vi.hoisted(() => ({ intake: vi.fn() }));
vi.mock('antd', async importOriginal => ({
    ...await importOriginal<typeof import('antd')>(),
    Select: ({ options, value, onChange, 'aria-label': label }: { options: { value: string | number; label: string }[]; value: string | number; onChange: (value: string | number) => void; 'aria-label'?: string }) =>
        <select aria-label={label} value={value} onChange={event => onChange(event.target.value)}>{options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}</select>,
}));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(async () => ({ success: true, data: [
    { id: 11, fullName: 'Bác sĩ Nội', specialtyId: 1, specialtyName: 'Nội' },
    { id: 22, fullName: 'Bác sĩ Nhi', specialtyId: 2, specialtyName: 'Nhi' },
] })) } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getFacilities: vi.fn(async () => ({ success: true, data: [{ id: 1, code: 'CS', name: 'Cơ sở' }] })),
    getDepartments: vi.fn(async () => ({ success: true, data: [{ id: 10, code: 'NOI', name: 'Nội', specialtyId: 1 }, { id: 20, code: 'NHI', name: 'Nhi', specialtyId: 2 }] })),
    getRooms: vi.fn(async () => ({ success: true, data: [] })),
} }));
vi.mock('../api/mpiApi', () => ({ mpiApi: { getPatientById: vi.fn(async () => ({ success: true, data: { id: 1, fullName: 'Người bệnh thử', dateOfBirth: '1990-01-01' } })) } }));
vi.mock('../api/patientVisitApi', () => ({ patientVisitApi: { receptionIntake: mocks.intake } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn() }) }));
vi.mock('../components/CheckInTicketModal', () => ({ CheckInTicketModal: () => null }));
beforeEach(() => { vi.clearAllMocks(); mocks.intake.mockResolvedValue({ success: true, data: {} }); });
afterEach(cleanup);
async function mount() {
    render(<MemoryRouter initialEntries={['/reception/register?existingPatientId=1']}><WalkInPatientRegistration /></MemoryRouter>);
    await screen.findByText('Người bệnh thử');
    fireEvent.click(screen.getByText(/Tiếp tục: Chuẩn bị lượt khám/));
    await screen.findByRole('option', { name: 'Bác sĩ Nội (Nội)' });
}
it('filters doctors by specialty, clears the previous doctor and shows the chosen name on confirmation', async () => {
    await mount();
    const doctor = screen.getByRole('combobox', { name: 'Bác sĩ phụ trách' });
    expect(screen.queryByRole('option', { name: 'Bác sĩ Nhi (Nhi)' })).not.toBeInTheDocument();
    fireEvent.change(doctor, { target: { value: '11' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Khoa phòng tiếp nhận' }), { target: { value: '20' } });
    await screen.findByRole('option', { name: 'Bác sĩ Nhi (Nhi)' });
    expect(doctor).toHaveValue('');
    expect(screen.queryByRole('option', { name: 'Bác sĩ Nội (Nội)' })).not.toBeInTheDocument();
    fireEvent.change(doctor, { target: { value: '22' } });
    fireEvent.change(screen.getByPlaceholderText(/Mô tả lý do đến khám/), { target: { value: 'Đau đầu' } });
    fireEvent.click(screen.getByText(/Tiếp tục: Xác nhận thông tin/));
    expect(screen.getByText('Bác sĩ Nhi')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Xác nhận tiếp nhận/ }));
    await waitFor(() => expect(mocks.intake).toHaveBeenCalledWith(expect.objectContaining({ assignedDoctorId: 22, departmentId: 20, priority: 1 }), expect.any(String)));
});
