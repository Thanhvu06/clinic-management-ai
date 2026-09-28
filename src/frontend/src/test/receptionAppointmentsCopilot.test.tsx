import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogProvider } from '../contexts/DialogContext';
import { CopilotResourceProvider, useCopilotResource } from '../components/copilot/copilotResourceContext';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import axiosClient from '../api/axiosClient';
import { organizationApi } from '../api/organizationApi';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/patientVisitApi', () => ({ patientVisitApi: { receptionCheckInAppointment: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getDepartments: vi.fn() } }));

const SelectionProbe: React.FC = () => {
    const { selection } = useCopilotResource();
    return <output data-testid="copilot-selection">{selection ? JSON.stringify(selection.context) : ''}</output>;
};

describe('ReceptionAppointments Copilot resource selection', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url.includes('/history')) return { success: true, data: [] } as never;
            return {
                success: true,
                data: {
                    items: [{ id: 77, appointmentCode: 'AP-77', patientId: 1, patientName: 'Người bệnh', patientPhone: '0900000000', doctorId: 2, doctorName: 'Bác sĩ A', specialtyId: 3, specialtyName: 'Nội khoa', appointmentDate: '2030-01-02', startTime: '08:00:00', endTime: '08:30:00', reason: 'Khám', status: 'Confirmed', facilityId: 9, facilityName: 'Cơ sở X' }],
                    totalItems: 1
                }
            } as never;
        });
        vi.mocked(organizationApi.getDepartments).mockResolvedValue({
            success: true,
            data: [{ id: 19, facilityId: 9, code: 'KHOA-NOI', name: 'Khoa Nội', isActive: true }]
        } as never);
    });

    it('uses a department explicitly selected from the appointment facility for Copilot check-in', async () => {
        render(<DialogProvider><CopilotResourceProvider><SelectionProbe /><ReceptionAppointments /></CopilotResourceProvider></DialogProvider>);
        await screen.findByText('AP-77');
        fireEvent.click(screen.getByRole('button', { name: /Chi tiết/i }));

        await waitFor(() => expect(organizationApi.getDepartments).toHaveBeenCalledWith(9));
        const department = await screen.findByRole('option', { name: /Khoa Nội/ });
        fireEvent.change(screen.getByRole('combobox', { name: /Khoa tiếp nhận cho Copilot/ }), { target: { value: '19' } });

        await waitFor(() => expect(screen.getByTestId('copilot-selection')).toHaveTextContent('"appointmentId":77'));
        expect(screen.getByTestId('copilot-selection')).toHaveTextContent('"departmentId":19');
        expect(department).toBeInTheDocument();
    });
});
