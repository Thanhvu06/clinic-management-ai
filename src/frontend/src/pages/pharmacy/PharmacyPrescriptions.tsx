import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Pill, CheckCircle2, Clock, Eye, X, AlertCircle, Printer } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface PrescriptionListItem {
    id: number;
    appointmentId: number;
    appointmentCode: string;
    appointmentDate: string;
    patientName: string;
    patientPhone: string;
    doctorName: string;
    status: string;
    itemCount: number;
    createdAt: string;
    dispensedAt?: string;
    notes?: string;
}

interface PrescriptionDetail {
    id: number;
    appointmentId: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    doctorId: number;
    doctorName: string;
    status: string;
    notes?: string;
    createdAt: string;
    dispensedAt?: string;
    items: PrescriptionItemDetail[];
}

interface PrescriptionItemDetail {
    medicineId: number;
    medicineCode: string;
    medicineName: string;
    unit: string;
    quantity: number;
    availableStock: number;
    isActive?: boolean;
    dosage: string;
    frequency: string;
    durationDays?: number;
    instructions?: string;
}

export const PharmacyPrescriptions: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [prescriptions, setPrescriptions] = useState<PrescriptionListItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('Issued'); // default to pending

    // Modal
    const [selectedPrescription, setSelectedPrescription] = useState<PrescriptionDetail | null>(null);
    const [detailModalOpen, setDetailModalOpen] = useState(false);
    const [detailLoading, setDetailLoading] = useState(false);
    const [dispenseLoading, setDispenseLoading] = useState(false);

    const fetchPrescriptions = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/pharmacy/prescriptions?${params.toString()}`);
            if (res.success && res.data) {
                setPrescriptions(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchPrescriptions();
    }, [page, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchPrescriptions();
    };

    const handleOpenDetail = async (id: number) => {
        setDetailModalOpen(true);
        setDetailLoading(true);
        try {
            const res = await axiosClient.get<any, ApiResponse<PrescriptionDetail>>(`/pharmacy/prescriptions/${id}`);
            if (res.success && res.data) {
                setSelectedPrescription(res.data);
            }
        } catch (error: any) {
            showAlert(error?.message || 'Không thể tải thông tin đơn thuốc.', 'Lỗi', 'error');
            setDetailModalOpen(false);
        } finally {
            setDetailLoading(false);
        }
    };

    const handleDispense = (id: number) => {
        showConfirm('Xác nhận cấp phát thuốc cho đơn này? Số lượng tồn kho tương ứng sẽ tự động bị khấu trừ.', async () => {
            setDispenseLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/pharmacy/prescriptions/${id}/dispense`);
                if (res.success) {
                    showAlert('Cấp phát thuốc thành công! Tồn kho đã được cập nhật.', 'Thành công', 'success');
                    setDetailModalOpen(false);
                    setSelectedPrescription(null);
                    fetchPrescriptions();
                }
            } catch (error: any) {
                const status = error?.response?.status;
                const errorCode = error?.response?.data?.errorCode;
                const errorMessage = error?.response?.data?.message || error?.message || 'Không thể cấp phát thuốc.';

                if (status === 409 || errorCode === 'PRESCRIPTION_ALREADY_DISPENSED' || errorCode === 'DISPENSE_CONFLICT') {
                    showAlert('Đơn thuốc này vừa được người khác xử lý hoặc dữ liệu đã thay đổi. Hệ thống sẽ tự động cập nhật lại danh sách.', 'Xung đột dữ liệu (409)', 'warning');
                    setDetailModalOpen(false);
                    setSelectedPrescription(null);
                    fetchPrescriptions();
                } else if (status === 422 || errorCode === 'INSUFFICIENT_MEDICINE_STOCK' || errorCode === 'MEDICINE_INACTIVE' || errorCode === 'PRESCRIPTION_NOT_DISPENSABLE') {
                    showAlert(errorMessage, 'Không thể cấp phát (422)', 'error');
                    handleOpenDetail(id);
                } else {
                    showAlert(errorMessage, 'Lỗi cấp phát', 'error');
                }
            } finally {
                setDispenseLoading(false);
            }
        });
    };

    const formatDateTime = (dateStr?: string) => {
        if (!dateStr) return '-';
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                dateStyle: 'short',
                timeStyle: 'short'
            }).format(new Date(dateStr));
        } catch {
            return dateStr;
        }
    };

    // Check if any item lacks sufficient stock or is inactive
    const hasInsufficientStock = selectedPrescription?.items.some(
        item => item.availableStock < item.quantity
    );
    const hasInactiveMedicine = selectedPrescription?.items.some(
        item => item.isActive === false
    );
    const canDispense = selectedPrescription?.status === 'Issued' && !hasInsufficientStock && !hasInactiveMedicine;

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '16px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}>
                    <Pill size={24} /> Quản lý cấp phát đơn thuốc
                </h2>
            </div>

            {/* Filter Card */}
            <div className="card" style={{ marginBottom: '24px' }}>
                <form onSubmit={handleSearchSubmit} style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <div style={{ flex: '1 1 250px' }}>
                        <div style={{ position: 'relative' }}>
                            <Search size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                            <input
                                type="text"
                                className="form-input"
                                placeholder="Tìm theo tên BN, SĐT hoặc mã lịch..."
                                style={{ paddingLeft: '40px' }}
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </div>
                    </div>
                    <div style={{ width: '220px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="Issued">Đang chờ cấp thuốc</option>
                            <option value="Dispensed">Đã hoàn thành cấp phát</option>
                        </select>
                    </div>
                    <button type="submit" className="btn-secondary">Tìm kiếm</button>
                </form>
            </div>

            {/* Table */}
            <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
                <div style={{ overflowX: 'auto' }}>
                    <table className="table">
                        <thead>
                            <tr>
                                <th>Mã đơn</th>
                                <th>Bệnh nhân</th>
                                <th>Bác sĩ chỉ định</th>
                                <th>Thời gian kê đơn</th>
                                <th>Số lượng thuốc</th>
                                <th>Trạng thái</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px' }}>Đang tải danh sách đơn thuốc...</td>
                                </tr>
                            ) : prescriptions.length === 0 ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px', color: 'var(--c-muted)' }}>
                                        Không có đơn thuốc nào phù hợp.
                                    </td>
                                </tr>
                            ) : (
                                prescriptions.map(p => (
                                    <tr key={p.id}>
                                        <td style={{ fontWeight: 600, color: 'var(--c-navy-dark)' }}>
                                            <div>#{p.id}</div>
                                            <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>{p.appointmentCode}</div>
                                        </td>
                                        <td>
                                            <div style={{ fontWeight: 600 }}>{p.patientName}</div>
                                            <div style={{ fontSize: '0.825rem', color: 'var(--c-muted)' }}>{p.patientPhone}</div>
                                        </td>
                                        <td>{p.doctorName}</td>
                                        <td style={{ fontSize: '0.85rem' }}>{formatDateTime(p.createdAt)}</td>
                                        <td>
                                            <span style={{ backgroundColor: '#f1f5f9', padding: '3px 8px', borderRadius: '6px', fontSize: '0.85rem', fontWeight: 600 }}>
                                                {p.itemCount} loại thuốc
                                            </span>
                                        </td>
                                        <td>
                                            {p.status === 'Dispensed' ? (
                                                <span className="badge badge-success" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                                    <CheckCircle2 size={12} /> Đã cấp thuốc
                                                </span>
                                            ) : (
                                                <span className="badge badge-warning" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                                    <Clock size={12} /> Chờ cấp thuốc
                                                </span>
                                            )}
                                        </td>
                                        <td style={{ textAlign: 'right' }}>
                                            <button
                                                className="btn-primary"
                                                style={{ padding: '6px 12px', fontSize: '0.85rem', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                                                onClick={() => handleOpenDetail(p.id)}
                                            >
                                                <Eye size={14} /> Xem & Cấp phát
                                            </button>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                {totalItems > 10 && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px 20px', borderTop: '1px solid var(--c-border)' }}>
                        <span style={{ fontSize: '0.875rem', color: 'var(--c-muted)' }}>Tổng số: {totalItems} đơn thuốc</span>
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
                                disabled={page * 10 >= totalItems}
                                onClick={() => setPage(p => p + 1)}
                            >
                                Trang sau
                            </button>
                        </div>
                    </div>
                )}
            </div>

            {/* Detail & Dispense Modal */}
            {detailModalOpen && (
                <div style={{
                    position: 'fixed',
                    inset: 0,
                    backgroundColor: 'rgba(15, 23, 42, 0.65)',
                    backdropFilter: 'blur(4px)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    zIndex: 9999,
                    padding: '16px'
                }}>
                    <div style={{
                        backgroundColor: 'white',
                        borderRadius: '16px',
                        width: '100%',
                        maxWidth: '740px',
                        maxHeight: '90vh',
                        overflowY: 'auto',
                        boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                        display: 'flex',
                        flexDirection: 'column'
                    }}>
                        {/* Header */}
                        <div style={{ padding: '20px 24px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <div>
                                <h3 style={{ margin: 0, fontSize: '1.25rem', color: 'var(--c-navy-dark)' }}>
                                    Chi tiết đơn thuốc #{selectedPrescription?.id}
                                </h3>
                                <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '4px' }}>
                                    Mã lịch hẹn: {selectedPrescription?.appointmentCode}
                                </div>
                            </div>
                            <button
                                onClick={() => { setDetailModalOpen(false); setSelectedPrescription(null); }}
                                style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}
                            >
                                <X size={20} />
                            </button>
                        </div>

                        {/* Body */}
                        <div style={{ padding: '24px' }}>
                            {detailLoading ? (
                                <div style={{ textAlign: 'center', padding: '40px', color: 'var(--c-muted)' }}>
                                    Đang tải dữ liệu đơn thuốc...
                                </div>
                            ) : selectedPrescription && (
                                <>
                                    {/* Patient & Doctor Banner */}
                                    <div style={{ backgroundColor: '#f8fafc', padding: '16px', borderRadius: '10px', display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px', marginBottom: '20px' }}>
                                        <div>
                                            <span style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>Bệnh nhân:</span>
                                            <div style={{ fontWeight: 700, color: '#0f172a' }}>{selectedPrescription.patientName}</div>
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>SĐT: {selectedPrescription.patientPhone}</div>
                                        </div>
                                        <div>
                                            <span style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>Bác sĩ chỉ định:</span>
                                            <div style={{ fontWeight: 700, color: '#0f172a' }}>{selectedPrescription.doctorName}</div>
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>Thời gian kê: {formatDateTime(selectedPrescription.createdAt)}</div>
                                        </div>
                                        <div>
                                            <span style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>Trạng thái cấp phát:</span>
                                            <div>
                                                {selectedPrescription.status === 'Dispensed' ? (
                                                    <span className="badge badge-success" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
                                                        <CheckCircle2 size={12} /> Đã cấp lúc {formatDateTime(selectedPrescription.dispensedAt)}
                                                    </span>
                                                ) : (
                                                    <span className="badge badge-warning" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
                                                        <Clock size={12} /> Chờ kiểm tra & cấp phát
                                                    </span>
                                                )}
                                            </div>
                                        </div>
                                    </div>

                                    {selectedPrescription.notes && (
                                        <div style={{ padding: '10px 14px', backgroundColor: '#eff6ff', borderRadius: '8px', fontSize: '0.875rem', color: '#1e40af', marginBottom: '16px' }}>
                                            <strong>Ghi chú từ Bác sĩ:</strong> {selectedPrescription.notes}
                                        </div>
                                    )}

                                    {/* Insufficient Stock Warning */}
                                    {hasInsufficientStock && selectedPrescription.status === 'Issued' && (
                                        <div style={{ padding: '12px 16px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '10px', color: '#991b1b', fontSize: '0.875rem', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '10px' }}>
                                            <AlertCircle size={20} />
                                            <span>
                                                <strong>Cảnh báo tồn kho:</strong> Có thuốc trong đơn không đủ số lượng tồn kho để cấp phát. Vui lòng nhập thêm hàng hoặc trao đổi lại với bác sĩ!
                                            </span>
                                        </div>
                                    )}

                                    {/* Inactive Medicine Warning */}
                                    {hasInactiveMedicine && selectedPrescription.status === 'Issued' && (
                                        <div style={{ padding: '12px 16px', backgroundColor: '#fffbeb', border: '1px solid #fde68a', borderRadius: '10px', color: '#92400e', fontSize: '0.875rem', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '10px' }}>
                                            <AlertCircle size={20} />
                                            <span>
                                                <strong>Cảnh báo danh mục:</strong> Có thuốc trong đơn đã ngừng cung cấp hoặc ngừng hoạt động. Không thể cấp phát đơn này!
                                            </span>
                                        </div>
                                    )}

                                    {/* Items Table */}
                                    <h4 style={{ margin: '0 0 10px', color: 'var(--c-navy-dark)' }}>Danh mục thuốc chỉ định</h4>
                                    <div style={{ overflowX: 'auto', border: '1px solid #e2e8f0', borderRadius: '8px', marginBottom: '20px' }}>
                                        <table className="table" style={{ margin: 0 }}>
                                            <thead style={{ backgroundColor: '#f8fafc' }}>
                                                <tr>
                                                    <th>Tên thuốc</th>
                                                    <th>Số lượng</th>
                                                    <th>Tồn kho</th>
                                                    <th>Liều dùng</th>
                                                    <th>Tần suất & Chỉ dẫn</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                {selectedPrescription.items.map((item, idx) => {
                                                    const isLow = item.availableStock < item.quantity;
                                                    const isInactive = item.isActive === false;
                                                    return (
                                                        <tr key={idx}>
                                                            <td>
                                                                <div style={{ fontWeight: 600 }}>{item.medicineName}</div>
                                                                <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                                    <span>{item.medicineCode}</span>
                                                                    {isInactive && (
                                                                        <span style={{ backgroundColor: '#fee2e2', color: '#991b1b', padding: '1px 6px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>
                                                                            Ngừng hoạt động
                                                                        </span>
                                                                    )}
                                                                </div>
                                                            </td>
                                                            <td style={{ fontWeight: 700 }}>
                                                                {item.quantity} {item.unit}
                                                            </td>
                                                            <td>
                                                                <span style={{
                                                                    padding: '3px 8px',
                                                                    borderRadius: '6px',
                                                                    fontSize: '0.825rem',
                                                                    fontWeight: 600,
                                                                    backgroundColor: isLow ? '#fee2e2' : '#dcfce7',
                                                                    color: isLow ? '#991b1b' : '#166534'
                                                                }}>
                                                                    {item.availableStock} {item.unit}
                                                                </span>
                                                            </td>
                                                            <td>{item.dosage}</td>
                                                            <td>
                                                                <div>{item.frequency}</div>
                                                                {item.instructions && (
                                                                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', fontStyle: 'italic' }}>
                                                                        {item.instructions}
                                                                    </div>
                                                                )}
                                                            </td>
                                                        </tr>
                                                    );
                                                })}
                                            </tbody>
                                        </table>
                                    </div>
                                </>
                            )}
                        </div>

                        {/* Footer */}
                        <div style={{ padding: '16px 24px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#f8fafc' }}>
                            <button
                                type="button"
                                className="btn-secondary"
                                onClick={() => window.print()}
                                style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                            >
                                <Printer size={16} /> In đơn thuốc
                            </button>

                            <div style={{ display: 'flex', gap: '10px' }}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    onClick={() => { setDetailModalOpen(false); setSelectedPrescription(null); }}
                                >
                                    Đóng
                                </button>
                                {selectedPrescription?.status === 'Issued' && (
                                    <button
                                        type="button"
                                        className="btn-primary"
                                        disabled={dispenseLoading || !canDispense}
                                        onClick={() => handleDispense(selectedPrescription.id)}
                                        style={{ backgroundColor: canDispense ? '#059669' : '#94a3b8', cursor: canDispense ? 'pointer' : 'not-allowed' }}
                                    >
                                        {dispenseLoading ? 'Đang xử lý...' : 'Xác nhận cấp thuốc & Trừ kho'}
                                    </button>
                                )}
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
