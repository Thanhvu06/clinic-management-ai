export const NotificationType = {
    System: 0,
    Appointment: 1,
    AppointmentChangeRequest: 2,
    Revisit: 3,
    LeaveRequest: 4,
    Prescription: 5,
    HealthPackage: 6,
    Invoice: 7,
    Payment: 8,
    Diagnostic: 9
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
