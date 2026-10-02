import axiosClient from './axiosClient';
import type { ApiResponse } from '../types';
import type { NotificationDto, UnreadNotificationCountDto, PagedResult } from '../types/notification';

export const notificationApi = {
    getMyNotifications: async (page = 1, pageSize = 20, isRead?: boolean): Promise<ApiResponse<PagedResult<NotificationDto>>> => {
        const params = new URLSearchParams();
        params.append('page', page.toString());
        params.append('pageSize', pageSize.toString());
        if (isRead !== undefined) {
            params.append('isRead', isRead.toString());
        }
        return axiosClient.get<any, ApiResponse<PagedResult<NotificationDto>>>(`/notifications?${params.toString()}`);
    },

    getUnreadCount: async (): Promise<ApiResponse<UnreadNotificationCountDto>> => {
        return axiosClient.get<any, ApiResponse<UnreadNotificationCountDto>>('/notifications/unread-count');
    },

    markAsRead: async (id: number): Promise<ApiResponse<void>> => {
        return axiosClient.patch<any, ApiResponse<void>>(`/notifications/${id}/read`);
    },

    markAllAsRead: async (): Promise<ApiResponse<number>> => {
        return axiosClient.patch<any, ApiResponse<number>>('/notifications/read-all');
    },
};
