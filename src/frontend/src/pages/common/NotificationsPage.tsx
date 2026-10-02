import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { 
    Bell, 
    Calendar, 
    FileText, 
    Pill, 
    UserX, 
    PackageCheck, 
    RefreshCw, 
    CheckCheck, 
    ChevronRight, 
    Inbox,
    RotateCcw,
    Check
} from 'lucide-react';
import { notificationApi } from '../../api/notificationApi';
import type { NotificationDto } from '../../types/notification';
import { NotificationType } from '../../types/notification';
import styles from './NotificationsPage.module.css';

export const NotificationsPage: React.FC = () => {
    const navigate = useNavigate();
    const [notifications, setNotifications] = useState<NotificationDto[]>([]);
    const [unreadCount, setUnreadCount] = useState<number>(0);
    const [activeTab, setActiveTab] = useState<'all' | 'unread' | 'read'>('all');
    const [page, setPage] = useState<number>(1);
    const [pageSize] = useState<number>(15);
    const [totalPages, setTotalPages] = useState<number>(1);
    const [totalItems, setTotalItems] = useState<number>(0);
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [isMarkingAll, setIsMarkingAll] = useState<boolean>(false);

    const fetchNotifications = useCallback(async () => {
        setIsLoading(true);
        try {
            const isReadParam = activeTab === 'unread' ? false : activeTab === 'read' ? true : undefined;
            const res = await notificationApi.getMyNotifications(page, pageSize, isReadParam);
            if (res.data) {
                setNotifications(res.data.items);
                setTotalPages(res.data.totalPages || 1);
                setTotalItems(res.data.totalItems || 0);
            }

            const unreadRes = await notificationApi.getUnreadCount();
            if (unreadRes.data) {
                setUnreadCount(unreadRes.data.unreadCount);
            }
        } catch (err) {
            console.error('Failed to load notifications', err);
        } finally {
            setIsLoading(false);
        }
    }, [page, pageSize, activeTab]);

    useEffect(() => {
        fetchNotifications();
    }, [fetchNotifications]);

    const handleTabChange = (tab: 'all' | 'unread' | 'read') => {
        setActiveTab(tab);
        setPage(1);
    };

    const handleMarkAsRead = async (e: React.MouseEvent, notif: NotificationDto) => {
        e.stopPropagation();
        if (notif.isRead) return;
        try {
            await notificationApi.markAsRead(notif.id);
            setUnreadCount((prev) => Math.max(0, prev - 1));
            setNotifications((prev) =>
                prev.map((n) => (n.id === notif.id ? { ...n, isRead: true } : n))
            );
        } catch (err) {
            console.error('Failed to mark notification as read', err);
        }
    };

    const handleItemClick = async (notif: NotificationDto) => {
        if (!notif.isRead) {
            try {
                await notificationApi.markAsRead(notif.id);
                setUnreadCount((prev) => Math.max(0, prev - 1));
                setNotifications((prev) =>
                    prev.map((n) => (n.id === notif.id ? { ...n, isRead: true } : n))
                );
            } catch (err) {
                console.error('Failed to mark notification as read', err);
            }
        }

        if (notif.route) {
            navigate(notif.route);
        }
    };

    const handleMarkAllAsRead = async () => {
        if (unreadCount === 0 || isMarkingAll) return;
        setIsMarkingAll(true);
        try {
            await notificationApi.markAllAsRead();
            setUnreadCount(0);
            setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })));
            if (activeTab === 'unread') {
                fetchNotifications();
            }
        } catch (err) {
            console.error('Failed to mark all as read', err);
        } finally {
            setIsMarkingAll(false);
        }
    };

    const formatDateTime = (isoString: string) => {
        try {
            const date = new Date(isoString);
            return date.toLocaleString('vi-VN', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric',
                hour: '2-digit',
                minute: '2-digit'
            });
        } catch {
            return '';
        }
    };

    const renderTypeIcon = (type: NotificationType) => {
        switch (type) {
            case NotificationType.Appointment:
                return <Calendar size={20} />;
            case NotificationType.AppointmentChangeRequest:
                return <RefreshCw size={20} />;
            case NotificationType.Revisit:
                return <FileText size={20} />;
            case NotificationType.LeaveRequest:
                return <UserX size={20} />;
            case NotificationType.Prescription:
                return <Pill size={20} />;
            case NotificationType.HealthPackage:
                return <PackageCheck size={20} />;
            default:
                return <Bell size={20} />;
        }
    };

    const getTypeLabel = (type: NotificationType) => {
        switch (type) {
            case NotificationType.Appointment: return 'Lịch khám';
            case NotificationType.AppointmentChangeRequest: return 'Thay đổi lịch';
            case NotificationType.Revisit: return 'Tái khám';
            case NotificationType.LeaveRequest: return 'Nghỉ phép';
            case NotificationType.Prescription: return 'Đơn thuốc / Khám';
            case NotificationType.HealthPackage: return 'Gói khám';
            default: return 'Hệ thống';
        }
    };

    return (
        <div className={styles.container}>
            <div className={styles.pageHeader}>
                <div className={styles.titleArea}>
                    <h1><Bell size={26} style={{ color: 'var(--c-primary, #0284c7)' }} /> Trung tâm thông báo</h1>
                    <p>Quản lý toàn bộ thông báo và cập nhật mới nhất từ phòng khám ClinicCare AI</p>
                </div>
                <div className={styles.headerActions}>
                    <button 
                        type="button" 
                        className={styles.actionBtn}
                        onClick={() => fetchNotifications()}
                        disabled={isLoading}
                    >
                        <RotateCcw size={16} /> Làm mới
                    </button>
                    <button 
                        type="button" 
                        className={`${styles.actionBtn} ${styles.primary}`}
                        onClick={handleMarkAllAsRead}
                        disabled={unreadCount === 0 || isMarkingAll}
                    >
                        <CheckCheck size={16} /> Đánh dấu tất cả đã đọc
                    </button>
                </div>
            </div>

            <div className={styles.tabsContainer}>
                <button 
                    type="button" 
                    className={`${styles.tabBtn} ${activeTab === 'all' ? styles.active : ''}`}
                    onClick={() => handleTabChange('all')}
                >
                    Tất cả
                </button>
                <button 
                    type="button" 
                    className={`${styles.tabBtn} ${activeTab === 'unread' ? styles.active : ''}`}
                    onClick={() => handleTabChange('unread')}
                >
                    Chưa đọc
                    {unreadCount > 0 && <span className={styles.tabBadge}>{unreadCount}</span>}
                </button>
                <button 
                    type="button" 
                    className={`${styles.tabBtn} ${activeTab === 'read' ? styles.active : ''}`}
                    onClick={() => handleTabChange('read')}
                >
                    Đã đọc
                </button>
            </div>

            {isLoading && notifications.length === 0 ? (
                <div className={styles.emptyBox}>
                    <RotateCcw size={36} className="animate-spin" />
                    <h3>Đang tải thông báo...</h3>
                </div>
            ) : notifications.length === 0 ? (
                <div className={styles.emptyBox}>
                    <Inbox size={48} />
                    <h3>Không có thông báo nào</h3>
                    <p>
                        {activeTab === 'unread' 
                            ? 'Bạn đã đọc toàn bộ thông báo.' 
                            : activeTab === 'read'
                            ? 'Chưa có thông báo nào được lưu trong mục đã đọc.'
                            : 'Hiện tại bạn chưa nhận được thông báo nào từ hệ thống.'}
                    </p>
                </div>
            ) : (
                <div className={styles.notificationsList}>
                    {notifications.map((n) => (
                        <div
                            key={n.id}
                            className={`${styles.card} ${!n.isRead ? styles.unread : ''}`}
                            onClick={() => handleItemClick(n)}
                        >
                            <div className={`${styles.iconWrapper} ${styles[`type${n.type}`] || ''}`}>
                                {renderTypeIcon(n.type)}
                            </div>
                            <div className={styles.content}>
                                <div className={styles.cardHeader}>
                                    <div className={styles.title}>
                                        <span>{n.title}</span>
                                        <span className={styles.typeBadge}>{getTypeLabel(n.type)}</span>
                                    </div>
                                    <span className={styles.time}>{formatDateTime(n.createdAtUtc)}</span>
                                </div>
                                <p className={styles.message}>{n.message}</p>
                                <div className={styles.cardActions}>
                                    {n.route ? (
                                        <span className={styles.routeHint}>
                                            Xem chi tiết liên quan <ChevronRight size={14} />
                                        </span>
                                    ) : (
                                        <span />
                                    )}
                                    {!n.isRead && (
                                        <button
                                            type="button"
                                            className={styles.markReadBtn}
                                            onClick={(e) => handleMarkAsRead(e, n)}
                                            title="Đánh dấu đã đọc thông báo này"
                                        >
                                            <Check size={14} /> Đánh dấu đã đọc
                                        </button>
                                    )}
                                </div>
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {totalPages > 1 && (
                <div className={styles.pagination}>
                    <span className={styles.pageInfo}>
                        Trang {page} / {totalPages} (Tổng cộng {totalItems} thông báo)
                    </span>
                    <div className={styles.pageButtons}>
                        <button
                            type="button"
                            className={styles.actionBtn}
                            onClick={() => setPage((p) => Math.max(1, p - 1))}
                            disabled={page <= 1 || isLoading}
                        >
                            Trang trước
                        </button>
                        <button
                            type="button"
                            className={styles.actionBtn}
                            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                            disabled={page >= totalPages || isLoading}
                        >
                            Trang sau
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
};
