import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { 
    Bell, 
    Calendar, 
    FileText, 
    Pill, 
    UserX, 
    PackageCheck, 
    RefreshCw, 
    CheckCheck,
    Inbox
} from 'lucide-react';
import { useAuth } from '../../auth/AuthContext';
import { notificationApi } from '../../api/notificationApi';
import type { NotificationDto } from '../../types/notification';
import { NotificationType } from '../../types/notification';
import styles from './NotificationBell.module.css';

export const NotificationBell: React.FC = () => {
    const { isAuthenticated, user } = useAuth();
    const navigate = useNavigate();
    const [isOpen, setIsOpen] = useState(false);
    const [unreadCount, setUnreadCount] = useState<number>(0);
    const [recentNotifications, setRecentNotifications] = useState<NotificationDto[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [isMarkingAll, setIsMarkingAll] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    const isPatient = user?.role === 'Patient';
    const viewAllPath = isPatient ? '/patient/notifications' : '/notifications';

    const fetchUnreadCount = useCallback(async () => {
        if (!isAuthenticated) return;
        try {
            const res = await notificationApi.getUnreadCount();
            if (res.data) {
                setUnreadCount(res.data.unreadCount);
            }
        } catch (err) {
            // Silently fail for polling
        }
    }, [isAuthenticated]);

    const fetchRecent = useCallback(async () => {
        if (!isAuthenticated) return;
        setIsLoading(true);
        try {
            const res = await notificationApi.getMyNotifications(1, 5);
            if (res.data && res.data.items) {
                setRecentNotifications(res.data.items);
            }
        } catch (err) {
            console.error('Failed to load recent notifications', err);
        } finally {
            setIsLoading(false);
        }
    }, [isAuthenticated]);

    // Initial load + polling every 45s
    useEffect(() => {
        if (!isAuthenticated) return;
        fetchUnreadCount();

        const interval = setInterval(() => {
            fetchUnreadCount();
        }, 45000);

        return () => clearInterval(interval);
    }, [isAuthenticated, fetchUnreadCount]);

    // Window focus refresh
    useEffect(() => {
        const handleFocus = () => {
            fetchUnreadCount();
            if (isOpen) {
                fetchRecent();
            }
        };

        window.addEventListener('focus', handleFocus);
        return () => window.removeEventListener('focus', handleFocus);
    }, [fetchUnreadCount, fetchRecent, isOpen]);

    // Close on click outside or escape key
    useEffect(() => {
        const handleClickOutside = (e: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
                setIsOpen(false);
            }
        };

        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                setIsOpen(false);
            }
        };

        if (isOpen) {
            document.addEventListener('mousedown', handleClickOutside);
            document.addEventListener('keydown', handleEscape);
        }

        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
            document.removeEventListener('keydown', handleEscape);
        };
    }, [isOpen]);

    const toggleDropdown = () => {
        const nextState = !isOpen;
        setIsOpen(nextState);
        if (nextState) {
            fetchRecent();
        }
    };

    const handleItemClick = async (notif: NotificationDto) => {
        if (!notif.isRead) {
            try {
                await notificationApi.markAsRead(notif.id);
                setUnreadCount((prev) => Math.max(0, prev - 1));
                setRecentNotifications((prev) =>
                    prev.map((n) => (n.id === notif.id ? { ...n, isRead: true } : n))
                );
            } catch (err) {
                console.error('Failed to mark notification as read', err);
            }
        }
        setIsOpen(false);
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
            setRecentNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })));
        } catch (err) {
            console.error('Failed to mark all as read', err);
        } finally {
            setIsMarkingAll(false);
        }
    };

    const formatRelativeTime = (isoString: string) => {
        try {
            const date = new Date(isoString);
            const now = new Date();
            const diffMs = now.getTime() - date.getTime();
            const diffMins = Math.floor(diffMs / 60000);
            const diffHours = Math.floor(diffMins / 60);
            const diffDays = Math.floor(diffHours / 24);

            if (diffMins < 1) return 'Vừa xong';
            if (diffMins < 60) return `${diffMins} phút trước`;
            if (diffHours < 24) return `${diffHours} giờ trước`;
            if (diffDays === 1) return 'Hôm qua';
            if (diffDays < 7) return `${diffDays} ngày trước`;

            return date.toLocaleDateString('vi-VN', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric'
            });
        } catch {
            return '';
        }
    };

    const renderTypeIcon = (type: NotificationType) => {
        switch (type) {
            case NotificationType.Appointment:
                return <Calendar size={16} />;
            case NotificationType.AppointmentChangeRequest:
                return <RefreshCw size={16} />;
            case NotificationType.Revisit:
                return <FileText size={16} />;
            case NotificationType.LeaveRequest:
                return <UserX size={16} />;
            case NotificationType.Prescription:
                return <Pill size={16} />;
            case NotificationType.HealthPackage:
                return <PackageCheck size={16} />;
            default:
                return <Bell size={16} />;
        }
    };

    if (!isAuthenticated) return null;

    return (
        <div className={styles.bellContainer} ref={dropdownRef}>
            <button
                type="button"
                className={`${styles.bellButton} ${isOpen ? styles.active : ''}`}
                onClick={toggleDropdown}
                aria-label={`Thông báo ${unreadCount > 0 ? `(${unreadCount} chưa đọc)` : ''}`}
                title="Thông báo"
            >
                <Bell size={20} />
                {unreadCount > 0 && (
                    <span className={styles.badge}>
                        {unreadCount > 99 ? '99+' : unreadCount}
                    </span>
                )}
            </button>

            {isOpen && (
                <div className={styles.dropdown}>
                    <div className={styles.header}>
                        <span className={styles.headerTitle}>
                            <Bell size={16} /> Thông báo
                        </span>
                        <button
                            type="button"
                            className={styles.markAllBtn}
                            onClick={handleMarkAllAsRead}
                            disabled={unreadCount === 0 || isMarkingAll}
                            title="Đánh dấu tất cả là đã đọc"
                        >
                            <CheckCheck size={14} style={{ display: 'inline', verticalAlign: 'middle', marginRight: 4 }} />
                            Đã đọc tất cả
                        </button>
                    </div>

                    <div className={styles.list}>
                        {isLoading && recentNotifications.length === 0 ? (
                            <div className={styles.emptyState}>
                                <span>Đang tải thông báo...</span>
                            </div>
                        ) : recentNotifications.length === 0 ? (
                            <div className={styles.emptyState}>
                                <Inbox size={32} style={{ opacity: 0.5 }} />
                                <span>Không có thông báo mới nào</span>
                            </div>
                        ) : (
                            recentNotifications.map((n) => (
                                <button
                                    type="button"
                                    key={n.id}
                                    className={`${styles.item} ${!n.isRead ? styles.unread : ''}`}
                                    onClick={() => handleItemClick(n)}
                                >
                                    <div className={`${styles.iconWrapper} ${styles[`type${n.type}`] || ''}`}>
                                        {renderTypeIcon(n.type)}
                                    </div>
                                    <div className={styles.content}>
                                        <div className={styles.itemHeader}>
                                            <span className={styles.title}>{n.title}</span>
                                            <span className={styles.time}>{formatRelativeTime(n.createdAtUtc)}</span>
                                        </div>
                                        <p className={styles.message}>{n.message}</p>
                                    </div>
                                    {!n.isRead && <span className={styles.unreadDot} />}
                                </button>
                            ))
                        )}
                    </div>

                    <div className={styles.footer}>
                        <Link 
                            to={viewAllPath} 
                            className={styles.viewAllLink}
                            onClick={() => setIsOpen(false)}
                        >
                            Xem tất cả thông báo
                        </Link>
                    </div>
                </div>
            )}
        </div>
    );
};
