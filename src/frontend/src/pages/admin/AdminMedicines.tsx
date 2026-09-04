import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Plus, Edit, Pill, X, CheckCircle2, XCircle, AlertTriangle } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface Medicine {
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
    reorderLevel: number;
    isActive: boolean;
    createdAt: string;
    updatedAt?: string;
}

export const AdminMedicines: React.FC = () => {
    const { showAlert } = useDialog();
    const [medicines, setMedicines] = useState<Medicine[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean; isEdit: boolean; data: Partial<Medicine> }>({
        isOpen: false,
        isEdit: false,
        data: {}
    });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');

    const fetchMedicines = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/medicines?${params.toString()}`);
            if (res.success && res.data) {
                setMedicines(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchMedicines();
    }, [page, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchMedicines();
    };

    const handleFormSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');
        setFormLoading(true);

        try {
            if (modal.isEdit) {
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/medicines/${modal.data.id}`, {
                    name: modal.data.name,
                    unit: modal.data.unit,
                    reorderLevel: Number(modal.data.reorderLevel) || 10,
                    isActive: modal.data.isActive ?? true
                });
                if (res.success) {
                    showAlert('Cập nhật thông tin thuốc thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchMedicines();
                }
            } else {
                const res = await axiosClient.post<any, ApiResponse<any>>('/admin/medicines', {
                    code: modal.data.code,
                    name: modal.data.name,
                    unit: modal.data.unit,
                    stockQuantity: Number(modal.data.stockQuantity) || 0,
                    reorderLevel: Number(modal.data.reorderLevel) || 10,
                    isActive: modal.data.isActive ?? true
                });
                if (res.success) {
                    showAlert('Thêm thuốc mới vào danh mục thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchMedicines();
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra khi lưu thông tin thuốc.');
        } finally {
            setFormLoading(false);
        }
    };

    const handleToggleStatus = async (id: number) => {
        try {
            const res = await axiosClient.patch<any, ApiResponse<any>>(`/admin/medicines/${id}/toggle-status`);
            if (res.success) {
                fetchMedicines();
            }
        } catch (error: any) {
            showAlert(error?.message || 'Không thể đổi trạng thái thuốc.', 'Lỗi', 'error');
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '16px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}>
                    <Pill size={24} /> Danh mục thuốc & Vật tư y tế
                </h2>
                <button
                    className="btn-primary"
                    onClick={() => setModal({
                        isOpen: true,
                        isEdit: false,
                        data: { isActive: true, stockQuantity: 100, reorderLevel: 20, unit: 'Viên' }
                    })}
                    style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                >
                    <Plus size={18} /> Thêm thuốc mới
                </button>
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
                                placeholder="Tìm theo tên thuốc hoặc mã..."
                                style={{ paddingLeft: '40px' }}
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </div>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="true">Đang sử dụng</option>
                            <option value="false">Tạm ngưng</option>
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
                                <th>Mã thuốc</th>
                                <th>Tên thuốc</th>
                                <th>Đơn vị tính</th>
                                <th>Tồn kho hiện tại</th>
                                <th>Mức cảnh báo</th>
                                <th>Trạng thái</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px' }}>Đang tải danh mục thuốc...</td>
                                </tr>
                            ) : medicines.length === 0 ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px', color: 'var(--c-muted)' }}>
                                        Không tìm thấy thuốc nào trong danh mục.
                                    </td>
                                </tr>
                            ) : (
                                medicines.map(med => {
                                    const isLowStock = med.stockQuantity <= med.reorderLevel;
                                    return (
                                        <tr key={med.id}>
                                            <td style={{ fontWeight: 600, color: 'var(--c-navy-dark)' }}>{med.code}</td>
                                            <td style={{ fontWeight: 600 }}>{med.name}</td>
                                            <td>
                                                <span style={{ backgroundColor: '#f1f5f9', padding: '3px 8px', borderRadius: '6px', fontSize: '0.825rem', color: '#475569' }}>
                                                    {med.unit}
                                                </span>
                                            </td>
                                            <td>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                    <span style={{ fontWeight: 700, color: isLowStock ? '#dc2626' : '#0f172a' }}>
                                                        {med.stockQuantity}
                                                    </span>
                                                    {isLowStock && (
                                                        <span title="Tồn kho dưới ngưỡng cảnh báo!" style={{ color: '#dc2626', display: 'inline-flex' }}>
                                                            <AlertTriangle size={15} />
                                                        </span>
                                                    )}
                                                </div>
                                            </td>
                                            <td style={{ color: 'var(--c-muted)' }}>{med.reorderLevel}</td>
                                            <td>
                                                {med.isActive ? (
                                                    <span className="badge badge-success" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                                        <CheckCircle2 size={12} /> Đang dùng
                                                    </span>
                                                ) : (
                                                    <span className="badge badge-danger" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                                        <XCircle size={12} /> Tạm ngưng
                                                    </span>
                                                )}
                                            </td>
                                            <td style={{ textAlign: 'right' }}>
                                                <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
                                                    <button
                                                        className="btn-secondary"
                                                        style={{ padding: '6px 10px', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                                                        onClick={() => setModal({ isOpen: true, isEdit: true, data: { ...med } })}
                                                        title="Chỉnh sửa"
                                                    >
                                                        <Edit size={14} /> Sửa
                                                    </button>
                                                    <button
                                                        className="btn-secondary"
                                                        style={{ padding: '6px 10px' }}
                                                        onClick={() => handleToggleStatus(med.id)}
                                                        title={med.isActive ? 'Ngưng sử dụng' : 'Mở lại'}
                                                    >
                                                        {med.isActive ? 'Khóa' : 'Mở'}
                                                    </button>
                                                </div>
                                            </td>
                                        </tr>
                                    );
                                })
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                {totalItems > 10 && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px 20px', borderTop: '1px solid var(--c-border)' }}>
                        <span style={{ fontSize: '0.875rem', color: 'var(--c-muted)' }}>Tổng số: {totalItems} thuốc</span>
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

            {/* Modal */}
            {modal.isOpen && (
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
                        maxHeight: '90vh',
                        overflowY: 'auto',
                        boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                        display: 'flex',
                        flexDirection: 'column'
                    }}>
                        <div style={{ padding: '20px 24px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <h3 style={{ margin: 0, fontSize: '1.2rem', color: 'var(--c-navy-dark)' }}>
                                {modal.isEdit ? 'Chỉnh sửa thông tin thuốc' : 'Thêm thuốc mới'}
                            </h3>
                            <button onClick={() => setModal({ isOpen: false, isEdit: false, data: {} })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}>
                                <X size={20} />
                            </button>
                        </div>

                        <form onSubmit={handleFormSubmit} style={{ padding: '24px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            {formError && (
                                <div style={{ padding: '10px 14px', backgroundColor: '#fef2f2', color: '#991b1b', borderRadius: '8px', fontSize: '0.875rem' }}>
                                    {formError}
                                </div>
                            )}

                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: '12px' }}>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Mã thuốc (*)</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        required
                                        disabled={modal.isEdit}
                                        value={modal.data.code || ''}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, code: e.target.value } })}
                                        placeholder="VD: PARA500"
                                    />
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Tên thuốc (*)</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        required
                                        value={modal.data.name || ''}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, name: e.target.value } })}
                                        placeholder="VD: Paracetamol 500mg"
                                    />
                                </div>
                            </div>

                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Đơn vị tính (*)</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        required
                                        value={modal.data.unit || ''}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, unit: e.target.value } })}
                                        placeholder="VD: Viên, Vỉ, Chai, Hộp"
                                    />
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Mức cảnh báo hết hàng</label>
                                    <input
                                        type="number"
                                        min={1}
                                        className="form-input"
                                        required
                                        value={modal.data.reorderLevel || 10}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, reorderLevel: Number(e.target.value) } })}
                                    />
                                </div>
                            </div>

                            {!modal.isEdit && (
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Số lượng tồn ban đầu</label>
                                    <input
                                        type="number"
                                        min={0}
                                        className="form-input"
                                        value={modal.data.stockQuantity || 0}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, stockQuantity: Number(e.target.value) } })}
                                    />
                                </div>
                            )}

                            <div>
                                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontWeight: 600, fontSize: '0.9rem' }}>
                                    <input
                                        type="checkbox"
                                        checked={modal.data.isActive ?? true}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, isActive: e.target.checked } })}
                                    />
                                    Kích hoạt cho phép kê đơn & cấp phát thuốc này
                                </label>
                            </div>

                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '12px' }}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    onClick={() => setModal({ isOpen: false, isEdit: false, data: {} })}
                                >
                                    Hủy
                                </button>
                                <button
                                    type="submit"
                                    className="btn-primary"
                                    disabled={formLoading}
                                >
                                    {formLoading ? 'Đang lưu...' : (modal.isEdit ? 'Lưu thay đổi' : 'Thêm thuốc')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
