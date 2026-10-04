import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { organizationApi } from '../api/organizationApi';
import { mpiApi } from '../api/mpiApi';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getFacilities: vi.fn(), getDepartments: vi.fn(), getRooms: vi.fn() } }));
vi.mock('../api/mpiApi', () => ({ mpiApi: { getPatientById: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn() }) }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });
const facilities = [
    { id: 1, code: 'A', name: 'Cơ sở A', address: 'A', city: '', phone: '', buildingCount: 0, departmentCount: 0, isActive: true },
    { id: 2, code: 'B', name: 'Cơ sở B', address: 'B', city: '', phone: '', buildingCount: 0, departmentCount: 0, isActive: true },
];
const doctorA = { id: 10, fullName: 'Bác sĩ A' };
const doctorB = { id: 20, fullName: 'Bác sĩ B' };
beforeEach(() => {
    vi.mocked(organizationApi.getFacilities).mockResolvedValue({ success: true, message: '', data: facilities });
    vi.mocked(organizationApi.getDepartments).mockResolvedValue({ success: true, message: '', data: [] });
    vi.mocked(organizationApi.getRooms).mockResolvedValue({ success: true, message: '', data: [] });
    vi.mocked(mpiApi.getPatientById).mockResolvedValue({ success: true, message: '', data: { id: 1, fullName: 'Người bệnh thử nghiệm' } } as any);
    vi.mocked(axiosClient.get).mockImplementation(async (_url, config) => ({ success: true, data: (config?.params as { facilityId?: number } | undefined)?.facilityId === 2 ? [doctorB] : [doctorA] }));
});
async function openForm() {
    render(<MemoryRouter initialEntries={['/reception/register?existingPatientId=1']}><WalkInPatientRegistration /></MemoryRouter>);
    await screen.findByText('Người bệnh thử nghiệm');
    fireEvent.click(screen.getByText(/Tiếp tục: Chuẩn bị lượt khám/));
    const doctor = screen.getByText('-- Tự động phân công theo ca trực --').closest('select')!;
    const facility = screen.getByRole('option', { name: 'Cơ sở A (A)' }).closest('select')!;
    return { doctor, facility };
}
describe('R3-6 facility-specific walk-in doctors', () => {
    it('requests the selected facility and reloads doctors, clearing an unavailable selection', async () => {
        const { doctor, facility } = await openForm();
        await screen.findByRole('option', { name: 'Bác sĩ A (Đa khoa)' });
        expect(axiosClient.get).toHaveBeenCalledWith('/doctors', expect.objectContaining({ params: { facilityId: 1 } }));
        fireEvent.change(doctor, { target: { value: '10' } });
        fireEvent.change(facility, { target: { value: '2' } });
        await screen.findByRole('option', { name: 'Bác sĩ B (Đa khoa)' });
        expect(axiosClient.get).toHaveBeenCalledWith('/doctors', expect.objectContaining({ params: { facilityId: 2 } }));
        expect(screen.queryByRole('option', { name: 'Bác sĩ A (Đa khoa)' })).not.toBeInTheDocument();
        expect(doctor).toHaveValue('');
    });
    it('retains a selected doctor that is also assigned to the new facility', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: [doctorA] });
        const { doctor, facility } = await openForm();
        await screen.findByRole('option', { name: 'Bác sĩ A (Đa khoa)' });
        fireEvent.change(doctor, { target: { value: '10' } });
        fireEvent.change(facility, { target: { value: '2' } });
        await waitFor(() => expect(axiosClient.get).toHaveBeenCalledWith('/doctors', expect.objectContaining({ params: { facilityId: 2 } })));
        expect(doctor).toHaveValue('10');
    });
    it('ignores a late response for the previous facility', async () => {
        let resolveOld!: (response: unknown) => void;
        const old = new Promise(resolve => { resolveOld = resolve; });
        vi.mocked(axiosClient.get).mockImplementation((_url, config) => (config?.params as { facilityId?: number } | undefined)?.facilityId === 2
            ? Promise.resolve({ success: true, data: [doctorB] }) : old);
        const { facility } = await openForm();
        fireEvent.change(facility, { target: { value: '2' } });
        await screen.findByRole('option', { name: 'Bác sĩ B (Đa khoa)' });
        await act(async () => { resolveOld({ success: true, data: [doctorA] }); await old; });
        expect(screen.getByRole('option', { name: 'Bác sĩ B (Đa khoa)' })).toBeInTheDocument();
        expect(screen.queryByRole('option', { name: 'Bác sĩ A (Đa khoa)' })).not.toBeInTheDocument();
    });
});
