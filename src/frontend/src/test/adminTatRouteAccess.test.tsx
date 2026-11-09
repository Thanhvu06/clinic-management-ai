import type { ReactNode } from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Outlet } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { isPathAllowedForRole } from '../utils/roleRoutes';

const mocks = vi.hoisted(() => ({ role: 'Admin', logout: vi.fn() }));
vi.mock('../auth/AuthContext', () => ({
    AuthProvider: ({ children }: { children: ReactNode }) => children,
    useAuth: () => ({ user: { fullName: 'Quản trị viên', role: mocks.role }, isAuthenticated: true, loading: false, logout: mocks.logout })
}));
vi.mock('../contexts/ChatContext', async original => ({
    ...await original<typeof import('../contexts/ChatContext')>(),
    ChatProvider: ({ children }: { children: ReactNode }) => children,
}));
vi.mock('../layouts/PatientChatBoundary', () => ({ PatientChatBoundary: () => null }));
vi.mock('../layouts/PublicLayout', () => ({ PublicLayout: () => <Outlet /> }));
vi.mock('../layouts/MainLayout', () => ({ MainLayout: () => <Outlet /> }));
vi.mock('../components/chat/ChatConsultationRedirect', () => ({
    ChatRouteHistory: ({ children }: { children: ReactNode }) => children,
    ChatConsultationRedirect: () => null
}));
vi.mock('../pages/lab/LabTurnaroundReport', () => ({
    LabTurnaroundReport: ({ readOnly }: { readOnly: boolean }) => <h1>{readOnly ? 'TAT chỉ đọc' : 'TAT trưởng khoa'}</h1>
}));
vi.mock('../pages/Forbidden', () => ({ Forbidden: () => <h1>Bạn không có quyền truy cập</h1> }));
vi.mock('../components/common/NotificationBell', () => ({ NotificationBell: () => null }));
vi.mock('../components/RoleCopilotPanel', () => ({ RoleCopilotPanel: () => null }));

beforeEach(() => { mocks.role = 'Admin'; window.history.replaceState({}, '', '/'); });
afterEach(() => { cleanup(); window.history.replaceState({}, '', '/'); });
const mount = (path: string) => {
    window.history.replaceState({}, '', path);
    return render(<ConfigProvider theme={{ token: { motion: false } }}><App /></ConfigProvider>);
};

describe('Admin TAT route access', () => {
    it('renders the actual App route for Admin in read-only mode', async () => {
        mount('/lab/tat');
        expect(await screen.findByRole('heading', { name: 'TAT chỉ đọc' })).toBeInTheDocument();
        expect(isPathAllowedForRole('Admin', '/lab/tat?from=2026-11-09')).toBe(true);
    });

    it.each(['/lab', '/lab/orders/1'])('still blocks Admin at %s', async path => {
        mount(path);
        expect(await screen.findByRole('heading', { name: 'Bạn không có quyền truy cập' })).toBeInTheDocument();
        expect(isPathAllowedForRole('Admin', path)).toBe(false);
    });

    it('preserves the existing LabSupervisor report route', async () => {
        mocks.role = 'LabSupervisor';
        mount('/lab/tat');
        expect(await screen.findByRole('heading', { name: 'TAT trưởng khoa' })).toBeInTheDocument();
    });

    it('adds only the TAT report link to the Admin menu', async () => {
        const { MainLayout } = await vi.importActual<typeof import('../layouts/MainLayout')>('../layouts/MainLayout');
        render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={['/admin']}>
            <MainLayout />
        </MemoryRouter></ConfigProvider>);
        expect(screen.getByRole('link', { name: 'Báo cáo trả kết quả' })).toHaveAttribute('href', '/lab/tat');
        expect(screen.queryByRole('link', { name: 'Tổng quan xét nghiệm' })).not.toBeInTheDocument();
    });
});
