import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Plus, Edit, Package, X, CheckCircle2, XCircle, Trash2 } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface HealthPackage {
    id: number;
    code: string;
    name: string;
    description: string;
    targetGroup: string;
    price: number;
    includedServices: string;
    imageUrl?: string;
    isActive: boolean;
    sortOrder: number;
}

export const AdminHealthPackages: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [packages, setPackages] = useState<HealthPackage[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean; isEdit: boolean; data: Partial<HealthPackage> }>({
        isOpen: false,
        isEdit: false,
        data: {}
    });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');

    const fetchPackages = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/health-packages?${params.toString()}`);
            if (res.success && res.data) {
                setPackages(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchPackages();
    }, [page, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchPackages();
    };

    const handleFormSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');
        setFormLoading(true);

        try {
            if (modal.isEdit) {
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/health-packages/${modal.data.id}`, {
                    name: modal.data.name,
                    description: modal.data.description,
                    targetGroup: modal.data.targetGroup,
                    price: Number(modal.data.price),
                    includedServices: modal.data.includedServices,
                    imageUrl: modal.data.imageUrl || '',
                    isActive: modal.data.isActive ?? true,
                    sortOrder: Number(modal.data.sortOrder) || 0
                });
                if (res.success) {
                    showAlert('Cập nhật gói khám thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchPackages();
                }
            } else {
                const res = await axiosClient.post<any, ApiResponse<any>>('/admin/health-packages', {
                    code: modal.data.code,
                    name: modal.data.name,
                    description: modal.data.description,
                    targetGroup: modal.data.targetGroup,
                    price: Number(modal.data.price),
                    includedServices: modal.data.includedServices,
                    imageUrl: modal.data.imageUrl || '',
                    isActive: modal.data.isActive ?? true,
                    sortOrder: Number(modal.data.sortOrder) || 0
                });
                if (res.success) {
                    showAlert('Thêm gói khám mới thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchPackages();
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra khi lưu gói khám.');
        } finally {
            setFormLoading(false);
        }
    };

    const handleDelete = (pkg: HealthPackage) => {
        showConfirm(`Bạn có chắc chắn muốn xóa gói khám "${pkg.name}"?`, async () => {
            try {
                const res = await axiosClient.delete<any, ApiResponse<any>>(`/admin/health-packages/${pkg.id}`);
                if (res.success) {
                    showAlert('Đã xóa gói khám thành công.', 'Thành công', 'success');
                    fetchPackages();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Không thể xóa gói khám này.', 'Lỗi', 'error');
            }
        });
    };

    const formatCurrency = (val: number) => {
        return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '16px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}>
                    <Package size={24} /> Quản lý gói khám sức khỏe
                </h2>
                <button
                    className="btn-primary"
                    onClick={() => setModal({
                        isOpen: true,
                        isEdit: false,
                        data: { isActive: true, sortOrder: 0, price: 1000000 }
                    })}
                    style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                >
                    <Plus size={18} /> Thêm gói khám mới
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
                                placeholder="Tìm theo tên hoặc mã gói..."
                                style={{ paddingLeft: '40px' }}
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </div>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="true">Đang hoạt động</option>
                            <option value="false">Tạm ẩn</option>
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
                                <th>Mã gói</th>
                                <th>Tên gói khám</th>
                                <th>Đối tượng phù hợp</th>
                                <th>Giá niêm yết</th>
                                <th>Thứ tự</th>
                                <th>Trạng thái</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px' }}>Đang tải dữ liệu...</td>
                                </tr>
                            ) : packages.length === 0 ? (
                                <tr>
                                    <td colSpan={7} style={{ textAlign: 'center', padding: '32px', color: 'var(--c-muted)' }}>
                                        Không tìm thấy gói khám nào phù hợp.
                                    </td>
                                </tr>
                            ) : (
                                packages.map(pkg => (
                                    <tr key={pkg.id}>
                                        <td style={{ fontWeight: 600, color: 'var(--c-navy-dark)' }}>{pkg.code}</td>
                                        <td>
                                            <div style={{ fontWeight: 600 }}>{pkg.name}</div>
                                            <div style={{ fontSize: '0.825rem', color: 'var(--c-muted)', maxWidth: '300px', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                {pkg.description}
                                            </div>
                                        </td>
                                        <td>
                                            <span style={{ backgroundColor: '#f1f5f9', padding: '3px 8px', borderRadius: '6px', fontSize: '0.8rem', color: '#475569' }}>
                                                {pkg.targetGroup || 'Mọi đối tượng'}
                                            </span>
                                        </td>
                                        <td style={{ fontWeight: 700, color: '#0284c7' }}>{formatCurrency(pkg.price)}</td>
                                        <td>{pkg.sortOrder}</td>
                                        <td>
                                            {pkg.isActive ? (
                                                <span className="badge badge-success" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                                    <CheckCircle2 size={12} /> Đang mở
                                                </span>
                                            ) : (
                                                <span className="badge badge-danger" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                                    <XCircle size={12} /> Đã tắt
                                                </span>
                                            )}
                                        </td>
                                        <td style={{ textAlign: 'right' }}>
                                            <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
                                                <button
                                                    className="btn-secondary"
                                                    style={{ padding: '6px 10px', display: 'inline-flex', alignItems: 'center', gap: '4px' }}
                                                    onClick={() => setModal({ isOpen: true, isEdit: true, data: { ...pkg } })}
                                                    title="Chỉnh sửa"
                                                >
                                                    <Edit size={14} /> Sửa
                                                </button>
                                                <button
                                                    className="btn-secondary"
                                                    style={{ padding: '6px 10px', color: 'var(--c-danger)', borderColor: '#fecaca' }}
                                                    onClick={() => handleDelete(pkg)}
                                                    title="Xóa gói khám"
                                                >
                                                    <Trash2 size={14} />
                                                </button>
                                            </div>
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
                        <span style={{ fontSize: '0.875rem', color: 'var(--c-muted)' }}>Tổng số: {totalItems} gói khám</span>
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
                        maxWidth: '600px',
                        maxHeight: '90vh',
                        overflowY: 'auto',
                        boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                        display: 'flex',
                        flexDirection: 'column'
                    }}>
                        <div style={{ padding: '20px 24px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <h3 style={{ margin: 0, fontSize: '1.2rem', color: 'var(--c-navy-dark)' }}>
                                {modal.isEdit ? 'Chỉnh sửa gói khám' : 'Thêm gói khám mới'}
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
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Mã gói (*)</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        required
                                        disabled={modal.isEdit}
                                        value={modal.data.code || ''}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, code: e.target.value } })}
                                        placeholder="VD: PKG-STANDARD"
                                    />
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Tên gói khám (*)</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        required
                                        value={modal.data.name || ''}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, name: e.target.value } })}
                                        placeholder="VD: Gói khám sức khỏe tổng quát tiêu chuẩn"
                                    />
                                </div>
                            </div>

                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Giá niêm yết (VNĐ) (*)</label>
                                    <input
                                        type="number"
                                        min={0}
                                        step={50000}
                                        className="form-input"
                                        required
                                        value={modal.data.price || 0}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, price: Number(e.target.value) } })}
                                    />
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Đối tượng phù hợp</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        value={modal.data.targetGroup || ''}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, targetGroup: e.target.value } })}
                                        placeholder="VD: Nam & Nữ mọi lứa tuổi"
                                    />
                                </div>
                            </div>

                            <div>
                                <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Mô tả ngắn</label>
                                <textarea
                                    className="form-textarea"
                                    rows={2}
                                    value={modal.data.description || ''}
                                    onChange={e => setModal({ ...modal, data: { ...modal.data, description: e.target.value } })}
                                    placeholder="Mô tả lợi ích của gói khám..."
                                />
                            </div>

                            <div>
                                <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Danh mục dịch vụ bao gồm (JSON hoặc danh sách)</label>
                                <textarea
                                    className="form-textarea"
                                    rows={3}
                                    value={modal.data.includedServices || ''}
                                    onChange={e => setModal({ ...modal, data: { ...modal.data, includedServices: e.target.value } })}
                                    placeholder='["Khám tổng quát", "Xét nghiệm máu 18 chỉ số", "Siêu âm bụng", "Chụp X-quang phổi"]'
                                />
                            </div>

                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', alignItems: 'center' }}>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '6px', fontWeight: 600, fontSize: '0.875rem' }}>Thứ tự ưu tiên hiển thị</label>
                                    <input
                                        type="number"
                                        className="form-input"
                                        value={modal.data.sortOrder ?? 0}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, sortOrder: Number(e.target.value) } })}
                                    />
                                </div>
                                <div style={{ paddingTop: '20px' }}>
                                    <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontWeight: 600, fontSize: '0.9rem' }}>
                                        <input
                                            type="checkbox"
                                            checked={modal.data.isActive ?? true}
                                            onChange={e => setModal({ ...modal, data: { ...modal.data, isActive: e.target.checked } })}
                                        />
                                        Kích hoạt mở bán gói này
                                    </label>
                                </div>
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
                                    {formLoading ? 'Đang lưu...' : (modal.isEdit ? 'Lưu thay đổi' : 'Tạo gói khám')}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
