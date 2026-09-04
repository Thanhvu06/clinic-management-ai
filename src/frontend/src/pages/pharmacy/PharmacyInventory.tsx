import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { History, Plus, Clock, User, ArrowDownRight, ArrowUpRight, RefreshCw, X, Pill } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface StockTransaction {
    id: number;
    medicineId: number;
    medicineCode: string;
    medicineName: string;
    unit: string;
    type: string;
    quantityChange: number;
    balanceAfter: number;
    prescriptionId?: number;
    reason?: string;
    actorName: string;
    createdAt: string;
}

interface ActiveMedicine {
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
}

export const PharmacyInventory: React.FC = () => {
    const { showAlert } = useDialog();
    const [transactions, setTransactions] = useState<StockTransaction[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Modal
    const [activeMedicines, setActiveMedicines] = useState<ActiveMedicine[]>([]);
    const [modalOpen, setModalOpen] = useState(false);
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');
    const [selectedMedId, setSelectedMedId] = useState<number>(0);
    const [transType, setTransType] = useState<number>(2); // 2: StockIn, 4: Adjustment
    const [qty, setQty] = useState<number>(100);
    const [reason, setReason] = useState<string>('');

    const fetchTransactions = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '15'
            });
            const res = await axiosClient.get<any, ApiResponse<any>>(`/pharmacy/inventory-transactions?${params.toString()}`);
            if (res.success && res.data) {
                setTransactions(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
        } finally {
            setLoading(false);
        }
    };

    const fetchMedicines = async () => {
        try {
            const res = await axiosClient.get<any, ApiResponse<ActiveMedicine[]>>('/medicines/active');
            if (res.success && res.data) {
                setActiveMedicines(res.data);
                if (res.data.length > 0 && selectedMedId === 0) {
                    setSelectedMedId(res.data[0].id);
                }
            }
        } catch {
            // Handled
        }
    };

    useEffect(() => {
        fetchTransactions();
        fetchMedicines();
    }, [page]);

    const handleFormSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');
        setFormLoading(true);

        try {
            const res = await axiosClient.post<any, ApiResponse<any>>('/pharmacy/inventory/adjust', {
                medicineId: selectedMedId,
                type: transType,
                quantity: Number(qty),
                reason: reason.trim()
            });

            if (res.success) {
                showAlert('Cập nhật tồn kho dược phẩm thành công.', 'Thành công', 'success');
                setModalOpen(false);
                setReason('');
                fetchTransactions();
                fetchMedicines();
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra khi cập nhật tồn kho.');
        } finally {
            setFormLoading(false);
        }
    };

    const formatDateTime = (dateStr: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                dateStyle: 'short',
                timeStyle: 'medium'
            }).format(new Date(dateStr));
        } catch {
            return dateStr;
        }
    };

    const getTypeBadge = (type: string, change: number) => {
        switch (type) {
            case 'StockIn':
                return (
                    <span style={{ backgroundColor: '#dcfce7', color: '#166534', padding: '3px 8px', borderRadius: '6px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <ArrowDownRight size={14} /> Nhập kho (+{change})
                    </span>
                );
            case 'Dispense':
                return (
                    <span style={{ backgroundColor: '#fee2e2', color: '#991b1b', padding: '3px 8px', borderRadius: '6px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <ArrowUpRight size={14} /> Xuất đơn ({change})
                    </span>
                );
            case 'Adjustment':
                return (
                    <span style={{ backgroundColor: '#fef3c7', color: '#92400e', padding: '3px 8px', borderRadius: '6px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        Kiểm kê ({change > 0 ? `+${change}` : change})
                    </span>
                );
            default:
                return (
                    <span style={{ backgroundColor: '#f1f5f9', color: '#475569', padding: '3px 8px', borderRadius: '6px', fontSize: '0.8rem', fontWeight: 600 }}>
                        {type} ({change})
                    </span>
                );
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '16px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}>
                    <History size={24} /> Lịch sử biến động kho & Quản lý nhập hàng
                </h2>
                <div style={{ display: 'flex', gap: '10px' }}>
                    <button
                        className="btn-secondary"
                        onClick={fetchTransactions}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                    >
                        <RefreshCw size={16} /> Làm mới
                    </button>
                    <button
                        className="btn-primary"
                        onClick={() => { setModalOpen(true); setFormError(''); }}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                    >
                        <Plus size={18} /> Nhập kho / Điều chỉnh tồn
                    </button>
                </div>
            </div>

            {/* Table */}
            <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
                <div style={{ overflowX: 'auto' }}>
                    <table className="table">
                        <thead>
                            <tr>
                                <th>Thời gian</th>
                                <th>Mặt hàng thuốc</th>
                                <th>Loại giao dịch</th>
                                <th>Số lượng biến động</th>
                                <th>Tồn sau giao dịch</th>
                                <th>Lý do / Căn cứ</th>
                                <th>Người thực hiện</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px' }}>Đang tải lịch sử giao dịch kho...</td>
                                </tr>
                            ) : transactions.length === 0 ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px', color: 'var(--c-muted)' }}>
                                        Chưa có giao dịch biến động tồn kho nào.
                                    </td>
                                </tr>
                            ) : (
                                transactions.map(t => (
                                    <tr key={t.id}>
                                        <td style={{ whiteSpace: 'nowrap', fontSize: '0.85rem' }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', color: 'var(--c-muted)' }}>
                                                <Clock size={14} />
                                                <span>{formatDateTime(t.createdAt)}</span>
                                            </div>
                                        </td>
                                        <td>
                                            <div style={{ fontWeight: 600 }}>{t.medicineName}</div>
                                            <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>{t.medicineCode}</div>
                                        </td>
                                        <td>{getTypeBadge(t.type, t.quantityChange)}</td>
                                        <td style={{ fontWeight: 700, color: t.quantityChange > 0 ? '#166534' : '#991b1b' }}>
                                            {t.quantityChange > 0 ? `+${t.quantityChange}` : t.quantityChange} {t.unit}
                                        </td>
                                        <td style={{ fontWeight: 700, color: '#0f172a' }}>
                                            {t.balanceAfter} {t.unit}
                                        </td>
                                        <td style={{ fontSize: '0.875rem' }}>
                                            {t.reason || (t.prescriptionId ? `Đơn thuốc #${t.prescriptionId}` : '-')}
                                        </td>
                                        <td>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                <User size={14} color="var(--c-primary)" />
                                                <span style={{ fontSize: '0.875rem' }}>{t.actorName}</span>
                                            </div>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                {totalItems > 15 && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px 20px', borderTop: '1px solid var(--c-border)' }}>
                        <span style={{ fontSize: '0.875rem', color: 'var(--c-muted)' }}>Tổng số: {totalItems} giao dịch</span>
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

            {/* Modal */}
            {modalOpen && (
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
                        maxWidth: '520px',
                        boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                        display: 'flex',
                        flexDirection: 'column',
                        overflow: 'hidden'
                    }}>
                        <div style={{ padding: '20px 24px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#f8fafc' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                <div style={{ width: '36px', height: '36px', borderRadius: '8px', backgroundColor: '#e0f2fe', color: '#0284c7', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                    <Pill size={20} />
                                </div>
                                <h3 style={{ margin: 0, fontSize: '1.2rem', color: 'var(--c-navy-dark)' }}>
                                    Nhập kho / Điều chỉnh tồn kho
                                </h3>
                            </div>
                            <button onClick={() => setModalOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '6px' }}>
                                <X size={20} />
                            </button>
                        </div>

                        <form onSubmit={handleFormSubmit} style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            {formError && (
                                <div style={{ padding: '10px 14px', backgroundColor: '#fef2f2', color: '#991b1b', borderRadius: '8px', fontSize: '0.875rem' }}>
                                    {formError}
                                </div>
                            )}

                            <div>
                                <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Chọn mặt hàng thuốc (*)</label>
                                <select
                                    className="form-select"
                                    value={selectedMedId}
                                    onChange={e => setSelectedMedId(Number(e.target.value))}
                                    required
                                >
                                    {activeMedicines.map(m => (
                                        <option key={m.id} value={m.id}>
                                            {m.name} ({m.code}) - Hiện tồn: {m.stockQuantity} {m.unit}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Loại tác vụ (*)</label>
                                    <select
                                        className="form-select"
                                        value={transType}
                                        onChange={e => setTransType(Number(e.target.value))}
                                    >
                                        <option value={2}>Nhập hàng thêm (Stock In)</option>
                                        <option value={4}>Kiểm kê điều chỉnh (Adjustment)</option>
                                    </select>
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Số lượng nhập (*)</label>
                                    <input
                                        type="number"
                                        min={1}
                                        className="form-input"
                                        required
                                        value={qty}
                                        onChange={e => setQty(Number(e.target.value))}
                                    />
                                </div>
                            </div>

                            <div>
                                <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Lý do / Nhà cung cấp / Mã phiếu nhập</label>
                                <textarea
                                    className="form-textarea"
                                    rows={3}
                                    value={reason}
                                    onChange={e => setReason(e.target.value)}
                                    placeholder="VD: Nhập 50 hộp từ Dược Hậu Giang theo hóa đơn HD-9821..."
                                />
                            </div>

                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '12px' }}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    onClick={() => setModalOpen(false)}
                                >
                                    Hủy
                                </button>
                                <button
                                    type="submit"
                                    className="btn-primary"
                                    disabled={formLoading}
                                >
                                    {formLoading ? 'Đang lưu...' : 'Xác nhận nhập kho'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
