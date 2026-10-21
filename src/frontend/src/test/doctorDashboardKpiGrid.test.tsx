import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import styles from '../pages/doctor/DoctorDashboard.module.css';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() })
}));

it('places all five doctor KPI cards directly in the CSS grid without ant columns', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, message: '', data: {
        kpis: { totalAppointmentsToday: 12, waitingCount: 5, inConsultationCount: 1, completedCount: 6, noShowCount: 0 },
        currentShift: null, nextPatient: null, todayQueue: [], upcomingAppointments: []
    } });
    const { container } = render(<MemoryRouter><DoctorDashboard /></MemoryRouter>);

    await screen.findByText('12');
    const grid = screen.getByText('Tổng số ca hôm nay').closest('.ant-card')?.parentElement;
    expect(grid).toHaveClass(styles.kpiGrid);
    expect(grid?.tagName).toBe('DIV');
    expect(grid?.children).toHaveLength(5);
    for (const title of ['Tổng số ca hôm nay', 'Đang chờ khám', 'Đang thăm khám', 'Đã hoàn tất', 'Vắng mặt (No-Show)']) {
        const card = within(grid!).getByText(title).closest('.ant-card');
        expect(card?.parentElement).toBe(grid);
    }
    expect(container.querySelector('.ant-col')).not.toBeInTheDocument();
});
