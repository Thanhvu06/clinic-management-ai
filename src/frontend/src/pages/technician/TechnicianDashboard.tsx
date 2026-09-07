import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { 
    Clock, CheckCircle, PlayCircle, Search, RefreshCw, 
    FileText, ArrowRight, FlaskConical 
} from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto, TechnicianDiagnosticStatsDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';

export const TechnicianDashboard: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert } = useDialog();

    const [orders, setOrders] = useState<DiagnosticOrderDto[]>([]);
    const [stats, setStats] = useState<TechnicianDiagnosticStatsDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [statusFilter, setStatusFilter] = useState<string>('');
    const [dateFilter, setDateFilter] = useState<string>('');
    const [searchTerm, setSearchTerm] = useState<string>('');
    const [page, setPage] = useState<number>(1);
    const [, setTotalItems] = useState<number>(0);

    const loadData = useCallback(async () => {
        setLoading(true);
        try {
            const [ordersRes, statsRes] = await Promise.all([
                diagnosticApi.getTechnicianOrders({
                    status: statusFilter || undefined,
                    date: dateFilter || undefined,
                    search: searchTerm || undefined,
                    page,
                    pageSize: 15
                }),
                diagnosticApi.getTechnicianStats()
            ]);

            if (ordersRes.success && ordersRes.data) {
                setOrders(ordersRes.data.items);
                setTotalItems(ordersRes.data.totalItems);
            }

            if (statsRes.success && statsRes.data) {
                setStats(statsRes.data);
            }
        } catch (err: any) {
            showAlert(err.message || 'Không thể tải danh sách chỉ định cận lâm sàng.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    }, [statusFilter, dateFilter, searchTerm, page, showAlert]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        loadData();
    };

    const statusBadge = (status: string) => {
        switch (status) {
            case 'Ordered':
                return <span style={{ padding: '4px 10px', borderRadius: '12px', background: '#fef3c7', color: '#b45309', fontSize: '12px', fontWeight: 600 }}>Chờ thực hiện</span>;
            case 'InProgress':
                return <span style={{ padding: '4px 10px', borderRadius: '12px', background: '#e0e7ff', color: '#4338ca', fontSize: '12px', fontWeight: 600 }}>Đang thực hiện</span>;
            case 'Completed':
                return <span style={{ padding: '4px 10px', borderRadius: '12px', background: '#dcfce7', color: '#15803d', fontSize: '12px', fontWeight: 600 }}>Đã hoàn tất</span>;
            case 'Cancelled':
                return <span style={{ padding: '4px 10px', borderRadius: '12px', background: '#f1f5f9', color: '#64748b', fontSize: '12px', fontWeight: 600 }}>Đã hủy</span>;
            default:
                return <span>{status}</span>;
        }
    };

    return (
        <div style={{ padding: '24px', maxWidth: '1200px', margin: '0 auto' }}>
            {/* Page Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <div>
                    <h1 style={{ fontSize: '24px', fontWeight: 'bold', margin: '0 0 4px', color: '#0f172a', display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <FlaskConical size={26} color="#0284c7" /> Bàn Làm Việc Cận Lâm Sàng
                    </h1>
                    <p style={{ margin: 0, color: '#64748b', fontSize: '14px' }}>
                        Quản lý tiếp nhận mẫu, thực hiện xét nghiệm & chẩn đoán hình ảnh
                    </p>
                </div>
                <button
                    onClick={loadData}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 16px', background: '#f1f5f9', border: '1px solid #cbd5e1', borderRadius: '6px', cursor: 'pointer', fontWeight: 500, color: '#334155' }}
                >
                    <RefreshCw size={15} /> Làm mới
                </button>
            </div>

            {/* KPI Stat Cards */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '16px', marginBottom: '24px' }}>
                <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '18px 20px', display: 'flex', alignItems: 'center', gap: '16px', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                    <div style={{ width: '48px', height: '48px', borderRadius: '8px', background: '#fef3c7', color: '#d97706', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        <Clock size={24} />
                    </div>
                    <div>
                        <div style={{ fontSize: '13px', color: '#64748b', fontWeight: 500 }}>Chờ thực hiện</div>
                        <div style={{ fontSize: '24px', fontWeight: 'bold', color: '#0f172a' }}>{stats?.orderedCount ?? 0}</div>
                    </div>
                </div>

                <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '18px 20px', display: 'flex', alignItems: 'center', gap: '16px', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                    <div style={{ width: '48px', height: '48px', borderRadius: '8px', background: '#e0e7ff', color: '#4f46e5', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        <PlayCircle size={24} />
                    </div>
                    <div>
                        <div style={{ fontSize: '13px', color: '#64748b', fontWeight: 500 }}>Đang thực hiện</div>
                        <div style={{ fontSize: '24px', fontWeight: 'bold', color: '#0f172a' }}>{stats?.inProgressCount ?? 0}</div>
                    </div>
                </div>

                <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '18px 20px', display: 'flex', alignItems: 'center', gap: '16px', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                    <div style={{ width: '48px', height: '48px', borderRadius: '8px', background: '#dcfce7', color: '#16a34a', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                        <CheckCircle size={24} />
                    </div>
                    <div>
                        <div style={{ fontSize: '13px', color: '#64748b', fontWeight: 500 }}>Hoàn tất hôm nay</div>
                        <div style={{ fontSize: '24px', fontWeight: 'bold', color: '#0f172a' }}>{stats?.completedTodayCount ?? 0}</div>
                    </div>
                </div>
            </div>

            {/* Filter Bar */}
            <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '16px', marginBottom: '20px', display: 'flex', gap: '12px', flexWrap: 'wrap', alignItems: 'center' }}>
                <form onSubmit={handleSearchSubmit} style={{ display: 'flex', gap: '8px', flex: 1, minWidth: '280px' }}>
                    <div style={{ position: 'relative', width: '100%' }}>
                        <Search size={16} color="#94a3b8" style={{ position: 'absolute', left: '12px', top: '10px' }} />
                        <input
                            type="text"
                            value={searchTerm}
                            onChange={e => setSearchTerm(e.target.value)}
                            placeholder="Tìm theo mã phiếu, mã hẹn, tên bệnh nhân, SĐT..."
                            style={{ width: '100%', padding: '8px 12px 8px 36px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none' }}
                        />
                    </div>
                    <button type="submit" style={{ padding: '8px 16px', background: '#0284c7', color: '#ffffff', border: 'none', borderRadius: '6px', fontSize: '13px', fontWeight: 600, cursor: 'pointer' }}>
                        Tìm
                    </button>
                </form>

                <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                    <select
                        value={statusFilter}
                        onChange={e => { setStatusFilter(e.target.value); setPage(1); }}
                        style={{ padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none', background: '#fff' }}
                    >
                        <option value="">Tất cả trạng thái</option>
                        <option value="Ordered">Chờ thực hiện</option>
                        <option value="InProgress">Đang thực hiện</option>
                        <option value="Completed">Đã hoàn tất</option>
                        <option value="Cancelled">Đã hủy</option>
                    </select>

                    <input
                        type="date"
                        value={dateFilter}
                        onChange={e => { setDateFilter(e.target.value); setPage(1); }}
                        style={{ padding: '7px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none' }}
                    />

                    {(statusFilter || dateFilter || searchTerm) && (
                        <button
                            type="button"
                            onClick={() => { setStatusFilter(''); setDateFilter(''); setSearchTerm(''); setPage(1); }}
                            style={{ padding: '8px 12px', background: '#f1f5f9', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '12px', cursor: 'pointer', color: '#64748b' }}
                        >
                            Xóa lọc
                        </button>
                    )}
                </div>
            </div>

            {/* Orders Table */}
            <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', overflow: 'hidden', boxShadow: '0 1px 3px rgba(0,0,0,0.05)' }}>
                {loading ? (
                    <div style={{ padding: '60px 0', textAlign: 'center', color: '#64748b' }}>Đang tải danh sách chỉ định...</div>
                ) : orders.length === 0 ? (
                    <div style={{ padding: '60px 0', textAlign: 'center', color: '#64748b' }}>
                        <FileText size={40} style={{ margin: '0 auto 12px', opacity: 0.4 }} />
                        <div style={{ fontSize: '15px', fontWeight: 600 }}>Không tìm thấy phiếu chỉ định nào</div>
                        <div style={{ fontSize: '13px', marginTop: '4px' }}>Thử thay đổi bộ lọc hoặc tìm kiếm lại.</div>
                    </div>
                ) : (
                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                        <thead>
                            <tr style={{ background: '#f8fafc', borderBottom: '1px solid #e2e8f0', color: '#475569' }}>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 600 }}>Mã phiếu</th>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 600 }}>Bệnh nhân</th>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 600 }}>Bác sĩ & Chuyên khoa</th>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 600 }}>Dịch vụ chỉ định</th>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontWeight: 600 }}>Thời gian</th>
                                <th style={{ padding: '12px 16px', textAlign: 'center', fontWeight: 600 }}>Trạng thái</th>
                                <th style={{ padding: '12px 16px', textAlign: 'right', fontWeight: 600 }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {orders.map(order => (
                                <tr key={order.id} style={{ borderBottom: '1px solid #f1f5f9', transition: 'background 0.15s' }}>
                                    <td style={{ padding: '14px 16px' }}>
                                        <span style={{ fontWeight: 'bold', color: '#0284c7' }}>{order.orderCode}</span>
                                        <div style={{ fontSize: '11px', color: '#94a3b8', marginTop: '2px' }}>#{order.appointmentCode}</div>
                                    </td>
                                    <td style={{ padding: '14px 16px' }}>
                                        <div style={{ fontWeight: 600, color: '#0f172a' }}>{order.patientName}</div>
                                        <div style={{ fontSize: '12px', color: '#64748b' }}>
                                            {order.patientPhone || 'Không có SĐT'} • {order.patientAge ? `${order.patientAge}t` : ''}
                                        </div>
                                    </td>
                                    <td style={{ padding: '14px 16px' }}>
                                        <div style={{ fontWeight: 500, color: '#334155' }}>{order.orderingDoctorName}</div>
                                        <div style={{ fontSize: '12px', color: '#64748b' }}>{order.specialtyName}</div>
                                    </td>
                                    <td style={{ padding: '14px 16px' }}>
                                        <div style={{ fontWeight: 500 }}>{order.items.length} dịch vụ</div>
                                        <div style={{ fontSize: '12px', color: '#64748b', maxWidth: '240px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                            {order.items.map(i => i.serviceName).join(', ')}
                                        </div>
                                    </td>
                                    <td style={{ padding: '14px 16px', color: '#475569' }}>
                                        {new Date(order.orderedAtUtc).toLocaleString('vi-VN', {
                                            hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit'
                                        })}
                                    </td>
                                    <td style={{ padding: '14px 16px', textAlign: 'center' }}>
                                        {statusBadge(order.status)}
                                    </td>
                                    <td style={{ padding: '14px 16px', textAlign: 'right' }}>
                                        <button
                                            onClick={() => navigate(`/diagnostics/orders/${order.id}`)}
                                            style={{
                                                display: 'inline-flex',
                                                alignItems: 'center',
                                                gap: '4px',
                                                padding: '6px 12px',
                                                borderRadius: '6px',
                                                fontSize: '12px',
                                                fontWeight: 600,
                                                cursor: 'pointer',
                                                border: 'none',
                                                background: order.status === 'Ordered' ? '#0284c7' : order.status === 'InProgress' ? '#4f46e5' : '#f1f5f9',
                                                color: order.status === 'Ordered' || order.status === 'InProgress' ? '#ffffff' : '#334155'
                                            }}
                                        >
                                            {order.status === 'Ordered' ? 'Tiếp nhận' : order.status === 'InProgress' ? 'Nhập kết quả' : 'Xem chi tiết'}
                                            <ArrowRight size={14} />
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>
        </div>
    );
};
