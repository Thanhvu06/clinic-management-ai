export const NotificationType = {
    System: 1,
    Appointment: 2,
    AppointmentChangeRequest: 3,
    Revisit: 4,
    LeaveRequest: 5,
    Prescription: 6,
    HealthPackage: 7
} as const;

export type NotificationType = typeof NotificationType[keyof typeof NotificationType];

export interface PagedResult<T> {
    items: T[];
    page: number;
    pageSize: number;
    totalItems: number;
    totalPages: number;
}

export interface NotificationDto {
    id: number;
    type: NotificationType;
    typeName: string;
    title: string;
    message: string;
    route?: string | null;
    relatedEntityType?: string | null;
    relatedEntityId?: string | null;
    isRead: boolean;
    createdAtUtc: string;
    readAtUtc?: string | null;
}

export interface UnreadNotificationCountDto {
    unreadCount: number;
}
