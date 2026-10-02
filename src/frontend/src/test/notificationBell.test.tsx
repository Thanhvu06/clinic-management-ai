import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { NotificationBell } from '../components/common/NotificationBell';
import { notificationApi } from '../api/notificationApi';

vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({
        isAuthenticated: true,
        user: { id: 'user-1', fullName: 'Nguyen Van A', role: 'Patient' },
        logout: vi.fn(),
    }),
}));

vi.mock('../api/notificationApi', () => ({
    notificationApi: {
        getUnreadCount: vi.fn(),
        getMyNotifications: vi.fn(),
        markAsRead: vi.fn(),
        markAllAsRead: vi.fn(),
    },
}));

describe('NotificationBell Component', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders bell button and badge when unreadCount > 0', async () => {
        vi.mocked(notificationApi.getUnreadCount).mockResolvedValueOnce({
            success: true,
            message: '',
            data: { unreadCount: 5 }
        });

        render(
            <MemoryRouter>
                <NotificationBell />
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('5')).toBeInTheDocument();
        });
    });

    it('renders 99+ when unreadCount exceeds 99', async () => {
        vi.mocked(notificationApi.getUnreadCount).mockResolvedValueOnce({
            success: true,
            message: '',
            data: { unreadCount: 150 }
        });

        render(
            <MemoryRouter>
                <NotificationBell />
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('99+')).toBeInTheDocument();
        });
    });

    it('toggles dropdown and fetches recent notifications on click', async () => {
        vi.mocked(notificationApi.getUnreadCount).mockResolvedValueOnce({
            success: true,
            message: '',
            data: { unreadCount: 1 }
        });

        vi.mocked(notificationApi.getMyNotifications).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                items: [
                    {
                        id: 101,
                        type: 2,
                        typeName: 'Appointment',
                        title: 'Đặt lịch khám thành công',
                        message: 'Lịch khám #APPT-001 đã tiếp nhận.',
                        route: '/patient/appointments',
                        isRead: false,
                        createdAtUtc: new Date().toISOString()
                    }
                ],
                page: 1,
                pageSize: 5,
                totalItems: 1,
                totalPages: 1
            }
        });

        render(
            <MemoryRouter>
                <NotificationBell />
            </MemoryRouter>
        );

        const bellBtn = screen.getByRole('button', { name: /thông báo/i });
        fireEvent.click(bellBtn);

        await waitFor(() => {
            expect(screen.getByText('Đặt lịch khám thành công')).toBeInTheDocument();
            expect(screen.getByText('Lịch khám #APPT-001 đã tiếp nhận.')).toBeInTheDocument();
            expect(screen.getByText('Xem tất cả thông báo')).toBeInTheDocument();
        });
    });
});
