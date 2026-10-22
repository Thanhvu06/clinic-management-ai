import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Select } from 'antd';
import { CheckCheck, ChevronRight, RotateCcw, Check } from 'lucide-react';
import { notificationApi } from '../../api/notificationApi';
import type { NotificationDto } from '../../types/notification';
import { NotificationType } from '../../types/notification';
import { notificationTypeIcon, notificationTypeLabel, notificationTypeTone } from '../../utils/notificationLabels';
import { DialogProvider, useDialog } from '../../contexts/DialogContext';
import { EmptyState, InlineError, LoadingState, PageHeader } from '../../components/common';
import styles from './NotificationsPage.module.css';

const errorMessage = (error: unknown, fallback: string) =>
    error && typeof error === 'object' && 'message' in error && typeof error.message === 'string'
        ? error.message : fallback;

const NotificationsContent: React.FC = () => {
    const navigate = useNavigate();
    const { showToast } = useDialog();
    const [notifications, setNotifications] = useState<NotificationDto[]>([]);
    const [unreadCount, setUnreadCount] = useState(0);
    const [activeTab, setActiveTab] = useState<'all' | 'unread' | 'read'>('all');
    const [selectedType, setSelectedType] = useState<NotificationType>();
    const [page, setPage] = useState(1);
    const pageSize = 15;
    const [totalPages, setTotalPages] = useState(1);
    const [totalItems, setTotalItems] = useState(0);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [isMarkingAll, setIsMarkingAll] = useState(false);
    const [markingIds, setMarkingIds] = useState<Set<number>>(new Set());
    const markingRef = useRef(new Set<number>());
    const requestRef = useRef(0);
    const readVersionRef = useRef(0);

    const fetchNotifications = useCallback(async () => {
        const request = ++requestRef.current;
        const readVersion = readVersionRef.current;
        setIsLoading(true);
        setError(null);
        // A count failure or slow response must not block the list.
        void notificationApi.getUnreadCount().then(res => {
            if (request === requestRef.current && readVersion === readVersionRef.current && res.success && res.data) {
                setUnreadCount(res.data.unreadCount);
            }
        }).catch(() => {});
        try {
            const isRead = activeTab === 'unread' ? false : activeTab === 'read' ? true : undefined;
            const res = await notificationApi.getMyNotifications(page, pageSize, isRead, selectedType);
            if (request !== requestRef.current) return;
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải thông báo.');
            setNotifications(res.data.items);
            setTotalPages(res.data.totalPages || 1);
            setTotalItems(res.data.totalItems || 0);
        } catch (err) {
            if (request === requestRef.current) setError(errorMessage(err, 'Không thể tải thông báo.'));
        } finally {
            if (request === requestRef.current) setIsLoading(false);
        }
    }, [page, activeTab, selectedType]);

    useEffect(() => {
        void fetchNotifications();
        return () => { requestRef.current++; };
    }, [fetchNotifications]);

    const handleTabChange = (tab: 'all' | 'unread' | 'read') => {
        if (tab === activeTab) return;
        setIsLoading(true);
        setActiveTab(tab);
        setPage(1);
    };

    const handleTypeChange = (type: NotificationType | 'all') => {
        setIsLoading(true);
        setSelectedType(type === 'all' ? undefined : type);
        setPage(1);
    };

    const markAsRead = async (notif: NotificationDto) => {
        if (notif.isRead) return true;
        if (markingRef.current.has(notif.id) || isMarkingAll) return false;
        markingRef.current.add(notif.id);
        setMarkingIds(new Set(markingRef.current));
        try {
            const res = await notificationApi.markAsRead(notif.id);
            if (!res.success) throw new Error(res.message || 'Không thể đánh dấu thông báo đã đọc.');
            readVersionRef.current++;
            setUnreadCount(prev => Math.max(0, prev - 1));
            setNotifications(prev => prev.map(n => n.id === notif.id ? { ...n, isRead: true } : n));
            return true;
        } catch (err) {
            showToast(errorMessage(err, 'Không thể đánh dấu thông báo đã đọc.'), 'error');
            return false;
        } finally {
            markingRef.current.delete(notif.id);
            setMarkingIds(new Set(markingRef.current));
        }
    };

    const handleItemClick = async (notif: NotificationDto) => {
        if (!await markAsRead(notif)) return;
        if (notif.route) navigate(notif.route);
    };

    const handleMarkAllAsRead = async () => {
        if (unreadCount === 0 || isMarkingAll || markingRef.current.size > 0) return;
        setIsMarkingAll(true);
        try {
            const res = await notificationApi.markAllAsRead();
            if (!res.success) throw new Error(res.message || 'Không thể đánh dấu tất cả thông báo đã đọc.');
            readVersionRef.current++;
            setUnreadCount(0);
            setNotifications(prev => prev.map(n => ({ ...n, isRead: true })));
            if (activeTab === 'unread') void fetchNotifications();
        } catch (err) {
            showToast(errorMessage(err, 'Không thể đánh dấu tất cả thông báo đã đọc.'), 'error');
        } finally {
            setIsMarkingAll(false);
        }
    };

    const formatDateTime = (isoString: string) => {
        try {
            return new Date(isoString).toLocaleString('vi-VN', {
                day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
            });
        } catch { return ''; }
    };

    const hasTypeFilter = selectedType !== undefined;
    const emptyDescription = hasTypeFilter
        ? `Không có thông báo thuộc loại ${notificationTypeLabel(selectedType)} trong mục này.`
        : activeTab === 'unread' ? 'Bạn đã đọc toàn bộ thông báo.'
        : activeTab === 'read' ? 'Chưa có thông báo nào được lưu trong mục đã đọc.'
        : 'Hiện tại bạn chưa nhận được thông báo nào từ hệ thống.';

    return (
        <div className={styles.container}>
            <PageHeader title="Thông báo" actions={<>
                <button type="button" className={styles.actionBtn} onClick={() => void fetchNotifications()} disabled={isLoading}>
                    <RotateCcw size={16} /> {isLoading ? 'Đang tải...' : 'Làm mới'}
                </button>
                <button type="button" className={`${styles.actionBtn} ${styles.primary}`}
                    onClick={() => void handleMarkAllAsRead()} disabled={unreadCount === 0 || isMarkingAll || markingIds.size > 0}>
                    <CheckCheck size={16} /> {isMarkingAll ? 'Đang xử lý...' : 'Đánh dấu tất cả đã đọc'}
                </button>
            </>} />
            <div className={styles.filterBar}>
                <div className={styles.tabsContainer}>
                    <button type="button" className={`${styles.tabBtn} ${activeTab === 'all' ? styles.active : ''}`}
                        aria-pressed={activeTab === 'all'} onClick={() => handleTabChange('all')}>Tất cả</button>
                    <button type="button" className={`${styles.tabBtn} ${activeTab === 'unread' ? styles.active : ''}`}
                        aria-pressed={activeTab === 'unread'} onClick={() => handleTabChange('unread')}>
                        Chưa đọc {unreadCount > 0 && <span className={styles.tabBadge}>{unreadCount}</span>}
                    </button>
                    <button type="button" className={`${styles.tabBtn} ${activeTab === 'read' ? styles.active : ''}`}
                        aria-pressed={activeTab === 'read'} onClick={() => handleTabChange('read')}>Đã đọc</button>
                </div>
                <div className={styles.typeControls}>
                    <Select<NotificationType | 'all'> aria-label="Loại thông báo" className={styles.typeFilter} virtual={false}
                        value={selectedType ?? 'all'} onChange={handleTypeChange}
                        options={[{ value: 'all', label: 'Tất cả loại' }, ...Object.values(NotificationType).map(type => ({
                            value: type, label: notificationTypeLabel(type),
                        }))]} />
                    {hasTypeFilter && <Button onClick={() => handleTypeChange('all')}>Xóa lọc</Button>}
                </div>
            </div>
            {isLoading ? <LoadingState message="Đang tải thông báo..." />
                : error ? <InlineError message={error} onRetry={() => void fetchNotifications()} />
                : notifications.length === 0 ? (
                    <EmptyState title={hasTypeFilter ? emptyDescription : 'Không có thông báo nào'}
                        description={hasTypeFilter ? undefined : emptyDescription} />
                ) : (
                    <div className={styles.notificationsList}>
                        {notifications.map(n => {
                            const TypeIcon = notificationTypeIcon(n.type);
                            return (
                                <div key={n.id} className={`${styles.card} ${!n.isRead ? styles.unread : ''}`}
                                    role="button" tabIndex={0} aria-label={n.title}
                                    onClick={() => void handleItemClick(n)}
                                    onKeyDown={e => {
                                        if (e.target === e.currentTarget && (e.key === 'Enter' || e.key === ' ')) {
                                            e.preventDefault();
                                            void handleItemClick(n);
                                        }
                                    }}>
                                    <div className={`${styles.iconWrapper} ${styles[notificationTypeTone(n.type)]}`}>
                                        <TypeIcon size={20} />
                                    </div>
                                    <div className={styles.content}>
                                        <div className={styles.cardHeader}>
                                            <div className={styles.title}>
                                                <span>{n.title}</span><span className={styles.typeBadge}>{notificationTypeLabel(n.type)}</span>
                                            </div>
                                            <span className={styles.time}>{formatDateTime(n.createdAtUtc)}</span>
                                        </div>
                                        <p className={styles.message}>{n.message}</p>
                                        <div className={styles.cardActions}>
                                            {n.route ? <span className={styles.routeHint}>Xem chi tiết liên quan <ChevronRight size={14} /></span> : <span />}
                                            {!n.isRead && (
                                                <button type="button" className={styles.markReadBtn}
                                                    disabled={markingIds.has(n.id) || isMarkingAll}
                                                    onClick={e => { e.stopPropagation(); void markAsRead(n); }}
                                                    onKeyDown={e => e.stopPropagation()}
                                                    title="Đánh dấu đã đọc thông báo này">
                                                    <Check size={14} /> {markingIds.has(n.id) ? 'Đang xử lý...' : 'Đánh dấu đã đọc'}
                                                </button>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                )}
            {!isLoading && !error && totalPages > 1 && (
                <div className={styles.pagination}>
                    <span className={styles.pageInfo}>Trang {page} / {totalPages} (Tổng cộng {totalItems} thông báo)</span>
                    <div className={styles.pageButtons}>
                        <button type="button" className={styles.actionBtn} disabled={page <= 1}
                            onClick={() => { setIsLoading(true); setPage(p => Math.max(1, p - 1)); }}>Trang trước</button>
                        <button type="button" className={styles.actionBtn} disabled={page >= totalPages}
                            onClick={() => { setIsLoading(true); setPage(p => Math.min(totalPages, p + 1)); }}>Trang sau</button>
                    </div>
                </div>
            )}
        </div>
    );
};

// Keep the page usable on its own, with visible feedback for failed read actions.
export const NotificationsPage: React.FC = () => <DialogProvider><NotificationsContent /></DialogProvider>;
