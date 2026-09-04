import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { History, RefreshCw, Clock, User } from 'lucide-react';

interface AuditLog {
    id: number;
    userId: string;
    userFullName: string;
    action: string;
    entityName: string;
    entityId: string;
    description: string;
    createdAt: string;
}

export const AdminAuditLogs: React.FC = () => {
    const [logs, setLogs] = useState<AuditLog[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [actionFilter, setActionFilter] = useState('');
    const [entityFilter, setEntityFilter] = useState('');

    const fetchLogs = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '15'
            });
            if (actionFilter) params.append('action', actionFilter);
            if (entityFilter) params.append('entityName', entityFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/audit-logs?${params.toString()}`);
            if (res.success && res.data) {
                setLogs(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchLogs();
    }, [page, actionFilter, entityFilter]);

    const formatDateTime = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                dateStyle: 'short',
                timeStyle: 'medium'
            }).format(new Date(dateString));
        } catch {
            return dateString;
        }
    };

    const getActionBadge = (action: string) => {
        const act = action.toUpperCase();
        if (act.includes('CREATE') || act.includes('ADD') || act.includes('INITIAL')) {
            return <span className="badge badge-success">{action}</span>;
        }
        if (act.includes('UPDATE') || act.includes('EDIT') || act.includes('CHANGE')) {
            return <span className="badge badge-info">{action}</span>;
        }
        if (act.includes('DELETE') || act.includes('CANCEL') || act.includes('REMOVE')) {
            return <span className="badge badge-danger">{action}</span>;
        }
        return <span className="badge badge-default">{action}</span>;
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '16px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}>
                    <History size={24} /> Nhật ký kiểm toán hệ thống (Audit Logs)
                </h2>
                <button
                    className="btn-secondary"
                    onClick={() => fetchLogs()}
                    style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                >
                    <RefreshCw size={16} /> Làm mới
                </button>
            </div>

            {/* Filter Card */}
            <div className="card" style={{ marginBottom: '24px' }}>
                <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <div style={{ flex: '1 1 200px' }}>
                        <select
                            className="form-select"
                            value={actionFilter}
                            onChange={e => { setActionFilter(e.target.value); setPage(1); }}
                        >
                            <option value="">Tất cả loại hành động</option>
                            <option value="Create">Create / Thêm mới</option>
                            <option value="Update">Update / Cập nhật</option>
                            <option value="Delete">Delete / Xóa</option>
                            <option value="StatusChange">StatusChange / Đổi trạng thái</option>
                            <option value="Login">Login / Đăng nhập</option>
                        </select>
                    </div>
                    <div style={{ flex: '1 1 200px' }}>
                        <select
                            className="form-select"
                            value={entityFilter}
                            onChange={e => { setEntityFilter(e.target.value); setPage(1); }}
                        >
                            <option value="">Tất cả đối tượng tác động</option>
                            <option value="User">User / Tài khoản</option>
                            <option value="Doctor">Doctor / Bác sĩ</option>
                            <option value="Specialty">Specialty / Chuyên khoa</option>
                            <option value="Appointment">Appointment / Lịch hẹn</option>
                            <option value="HealthPackage">HealthPackage / Gói khám</option>
                            <option value="Medicine">Medicine / Thuốc</option>
                        </select>
                    </div>
                </div>
            </div>

            {/* Table */}
            <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
                <div style={{ overflowX: 'auto' }}>
                    <table className="table">
                        <thead>
                            <tr>
                                <th>Thời gian</th>
                                <th>Người thực hiện</th>
                                <th>Hành động</th>
                                <th>Thực thể</th>
                                <th>Mã tham chiếu</th>
                                <th>Mô tả chi tiết</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={6} style={{ textAlign: 'center', padding: '32px' }}>Đang tải nhật ký kiểm toán...</td>
                                </tr>
                            ) : logs.length === 0 ? (
                                <tr>
                                    <td colSpan={6} style={{ textAlign: 'center', padding: '32px', color: 'var(--c-muted)' }}>
                                        Chưa có bản ghi nhật ký kiểm toán nào.
                                    </td>
                                </tr>
                            ) : (
                                logs.map(log => (
                                    <tr key={log.id}>
                                        <td style={{ whiteSpace: 'nowrap', fontSize: '0.85rem' }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', color: 'var(--c-muted)' }}>
                                                <Clock size={14} />
                                                <span>{formatDateTime(log.createdAt)}</span>
                                            </div>
                                        </td>
                                        <td>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontWeight: 600 }}>
                                                <User size={14} color="var(--c-primary)" />
                                                <span>{log.userFullName}</span>
                                            </div>
                                        </td>
                                        <td>{getActionBadge(log.action)}</td>
                                        <td>
                                            <span style={{ backgroundColor: '#f1f5f9', padding: '3px 8px', borderRadius: '6px', fontSize: '0.825rem', color: '#475569', fontWeight: 600 }}>
                                                {log.entityName}
                                            </span>
                                        </td>
                                        <td style={{ fontFamily: 'monospace', fontSize: '0.85rem' }}>{log.entityId || '-'}</td>
                                        <td style={{ fontSize: '0.9rem', color: 'var(--c-text-dark)' }}>{log.description}</td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                {totalItems > 15 && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px 20px', borderTop: '1px solid var(--c-border)' }}>
                        <span style={{ fontSize: '0.875rem', color: 'var(--c-muted)' }}>Tổng số: {totalItems} sự kiện ghi nhận</span>
                        <div style={{ display: 'flex', gap: '8px' }}>
                            <button
                                className="btn-secondary"
                                disabled={page === 1}
                                onClick={() => setPage(p => Math.max(1, p - 1))}
                            >
                                Trang trước
                            </button>
                            <span style={{ display: 'flex', alignItems: 'center', padding: '0 8px', fontSize: '0.9rem' }}>Trang {page}</span>
                            <button
                                className="btn-secondary"
                                disabled={page * 15 >= totalItems}
                                onClick={() => setPage(p => p + 1)}
                            >
                                Trang sau
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};
