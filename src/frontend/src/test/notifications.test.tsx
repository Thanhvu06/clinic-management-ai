import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { NotificationsPage } from '../pages/common/NotificationsPage';
import { notificationApi } from '../api/notificationApi';
import { type NotificationDto, NotificationType } from '../types/notification';

vi.mock('../api/notificationApi', () => ({
    notificationApi: {
        getMyNotifications: vi.fn(),
        getUnreadCount: vi.fn(),
        markAsRead: vi.fn(),
        markAllAsRead: vi.fn(),
    },
}));

const mockNotifications: NotificationDto[] = [
    {
        id: 1,
        type: NotificationType.Appointment,
        typeName: 'Appointment',
        title: 'Đặt lịch khám thành công',
        message: 'Lịch khám #APPT-2026-0001 đã được tiếp nhận.',
        route: '/patient/appointments',
        isRead: false,
        createdAtUtc: new Date().toISOString()
    },
    {
        id: 2,
        type: NotificationType.Prescription,
        typeName: 'Prescription',
        title: 'Đơn thuốc đã được phát',
        message: 'Đơn thuốc cho lịch khám đã được nhà thuốc cấp phát.',
        route: '/patient/prescriptions',
        isRead: true,
        createdAtUtc: new Date(Date.now() - 3600000).toISOString()
    }
];

describe('NotificationsPage Component', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders notification center header, tabs, and notification cards', async () => {
        vi.mocked(notificationApi.getMyNotifications).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                items: mockNotifications,
                page: 1,
                pageSize: 15,
                totalItems: 2,
                totalPages: 1
            }
        });

        vi.mocked(notificationApi.getUnreadCount).mockResolvedValueOnce({
            success: true,
            message: '',
            data: { unreadCount: 1 }
        });

        render(
            <MemoryRouter>
                <NotificationsPage />
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Trung tâm thông báo')).toBeInTheDocument();
            expect(screen.getByText('Đặt lịch khám thành công')).toBeInTheDocument();
            expect(screen.getByText('Đơn thuốc đã được phát')).toBeInTheDocument();
            expect(screen.getByText('1')).toBeInTheDocument(); // Unread tab badge
        });
    });

    it('calls markAsRead when individual mark read button is clicked', async () => {
        vi.mocked(notificationApi.getMyNotifications).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                items: mockNotifications,
                page: 1,
                pageSize: 15,
                totalItems: 2,
                totalPages: 1
            }
        });

        vi.mocked(notificationApi.getUnreadCount).mockResolvedValueOnce({
            success: true,
            message: '',
            data: { unreadCount: 1 }
        });

        vi.mocked(notificationApi.markAsRead).mockResolvedValueOnce({
            success: true,
            message: 'Đã đánh dấu thông báo là đã đọc.'
        });

        render(
            <MemoryRouter>
                <NotificationsPage />
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Đặt lịch khám thành công')).toBeInTheDocument();
        });

        const markReadBtn = screen.getByTitle('Đánh dấu đã đọc thông báo này');
        fireEvent.click(markReadBtn);

        await waitFor(() => {
            expect(notificationApi.markAsRead).toHaveBeenCalledWith(1);
        });
    });

    it('calls markAllAsRead when Mark All button is clicked', async () => {
        vi.mocked(notificationApi.getMyNotifications).mockResolvedValueOnce({
            success: true,
            message: '',
            data: {
                items: mockNotifications,
                page: 1,
                pageSize: 15,
                totalItems: 2,
                totalPages: 1
            }
        });

        vi.mocked(notificationApi.getUnreadCount).mockResolvedValueOnce({
            success: true,
            message: '',
            data: { unreadCount: 1 }
        });

        vi.mocked(notificationApi.markAllAsRead).mockResolvedValueOnce({
            success: true,
            message: 'Đã đánh dấu tất cả 1 thông báo là đã đọc.',
            data: 1
        });

        render(
            <MemoryRouter>
                <NotificationsPage />
            </MemoryRouter>
        );

        await waitFor(() => {
            expect(screen.getByText('Trung tâm thông báo')).toBeInTheDocument();
        });

        const markAllBtn = screen.getByText('Đánh dấu tất cả đã đọc');
        fireEvent.click(markAllBtn);

        await waitFor(() => {
            expect(notificationApi.markAllAsRead).toHaveBeenCalled();
        });
    });
});
