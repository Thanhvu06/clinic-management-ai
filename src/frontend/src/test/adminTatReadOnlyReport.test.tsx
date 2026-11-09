import { cleanup, render, screen } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { LabTurnaroundRoute } from '../pages/lab/LabTurnaroundRoute';

const mocks = vi.hoisted(() => ({ report: vi.fn() }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { role: 'Admin' } }) }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTurnaroundReport: mocks.report } }));
afterEach(cleanup);

it('renders report data for Admin without a link to LabSupervisor order actions', async () => {
    mocks.report.mockResolvedValue({ success: true, data: {
        from: '2026-11-03', to: '2026-11-09', category: null, orderCount: 1,
        segments: [{ key: 'Total', count: 1, medianMinutes: 60, p90Minutes: 60 }],
        withinTargetCount: 0, withinTargetPercent: 0, byService: [],
        slowestOrders: [{ orderId: 1, orderCode: 'DX-ADMIN-READ', patientName: 'Bệnh nhân kiểm thử',
            serviceNames: ['Xét nghiệm'], orderedAtUtc: '2026-11-09T01:00:00Z', completedAtUtc: '2026-11-09T02:00:00Z',
            totalMinutes: 60, targetMinutes: 45 }]
    } });
    render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter><LabTurnaroundRoute /></MemoryRouter></ConfigProvider>);
    expect(await screen.findByText('DX-ADMIN-READ')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'DX-ADMIN-READ' })).not.toBeInTheDocument();
    expect(mocks.report).toHaveBeenCalledTimes(1);
});
