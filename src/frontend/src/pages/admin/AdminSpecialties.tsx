import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Plus, Edit, Stethoscope, Sparkles, X, AlertTriangle } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface Specialty {
    id: number;
    specialtyCode: string;
    name: string;
    description: string;
    isActive: boolean;
    aiEnabled: boolean;
}

export const AdminSpecialties: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    
    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, isEdit: boolean, data: Partial<Specialty> }>({ isOpen: false, isEdit: false, data: {} });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');

    const fetchSpecialties = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/specialties?${params.toString()}`);
            if (res.success && res.data) {
                setSpecialties(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchSpecialties();
    }, [page, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchSpecialties();
    };

    const handleFormSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');
        
        const submitData = async () => {
            setFormLoading(true);
            try {
                if (modal.isEdit) {
                    const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/specialties/${modal.data.id}`, {
                        name: modal.data.name,
                        description: modal.data.description,
                        isActive: modal.data.isActive
                    });
                    if (res.success) {
                        showAlert('Cập nhật chuyên khoa thành công.', 'Thành công', 'success');
                        setModal({ isOpen: false, isEdit: false, data: {} });
                        fetchSpecialties();
                    }
                } else {
                    const res = await axiosClient.post<any, ApiResponse<any>>('/admin/specialties', {
                        specialtyCode: modal.data.specialtyCode,
                        name: modal.data.name,
                        description: modal.data.description,
                        isActive: modal.data.isActive ?? true
                    });
                    if (res.success) {
                        showAlert('Thêm chuyên khoa thành công.', 'Thành công', 'success');
                        setModal({ isOpen: false, isEdit: false, data: {} });
                        fetchSpecialties();
                    }
                }
            } catch (error: any) {
                setFormError(error?.message || 'Có lỗi xảy ra.');
            } finally {
                setFormLoading(false);
            }
        };

        if (modal.isEdit && !modal.data.isActive) {
            showConfirm('Cảnh báo: Chuyên khoa ngừng hoạt động sẽ không hiển thị khi bệnh nhân đặt lịch mới, nhưng lịch sử vẫn được giữ. Bạn có chắc chắn?', () => {
                submitData();
            });
        } else {
            submitData();
        }
    };

    const openCreateModal = () => {
        setModal({ isOpen: true, isEdit: false, data: { isActive: true } });
    };

    const openEditModal = (spec: Specialty) => {
        setModal({ isOpen: true, isEdit: true, data: { ...spec } });
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <Stethoscope size={24} /> Quản lý chuyên khoa
                </h2>
                <button className="btn-primary" onClick={openCreateModal}>
                    <Plus size={18} /> Thêm chuyên khoa
                </button>
            </div>

            <div className="card" style={{ marginBottom: '24px' }}>
                <form onSubmit={handleSearchSubmit} style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <div style={{ flex: '1 1 250px' }}>
                        <div style={{ position: 'relative' }}>
                            <Search size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                            <input 
                                type="text" 
                                className="form-input" 
                                placeholder="Tìm theo mã, tên..." 
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
                            <option value="false">Ngừng hoạt động</option>
                        </select>
                    </div>
                    <button type="submit" className="btn-secondary">Lọc</button>
                </form>
            </div>

            <div className="card table-responsive" style={{ padding: 0 }}>
                {loading ? (
                    <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải dữ liệu...</div>
                ) : (
                    <table className="table" style={{ width: '100%' }}>
                        <thead>
                            <tr>
                                <th>Mã Khoa</th>
                                <th>Tên chuyên khoa</th>
                                <th>Trạng thái</th>
                                <th>Tính năng AI</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {specialties.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Không tìm thấy dữ liệu.
                                    </td>
                                </tr>
                            ) : specialties.map(s => (
                                <tr key={s.id}>
                                    <td style={{ fontWeight: 500 }}>{s.specialtyCode}</td>
                                    <td>
                                        <div style={{ fontWeight: 600 }}>{s.name}</div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '4px', maxWidth: '300px', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                            {s.description || 'Không có mô tả'}
                                        </div>
                                    </td>
                                    <td>
                                        {s.isActive ? 
                                            <span className="badge badge-success">Đang hoạt động</span> : 
                                            <span className="badge badge-danger">Ngừng hoạt động</span>
                                        }
                                    </td>
                                    <td>
                                        {s.aiEnabled ? 
                                            <span className="badge badge-info" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                                                <Sparkles size={12}/> Cho phép gợi ý
                                            </span> : 
                                            <span className="badge badge-muted">Tắt</span>
                                        }
                                    </td>
                                    <td style={{ textAlign: 'right' }}>
                                        <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.85rem' }} onClick={() => openEditModal(s)}>
                                            <Edit size={14} style={{ marginRight: '4px' }}/> Sửa
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>
            
            <div style={{ marginTop: '16px', color: 'var(--c-muted)', fontSize: '0.9rem' }}>
                Tổng cộng: {totalItems} chuyên khoa
            </div>

            {/* Form Modal */}
            {modal.isOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: 'var(--radius-lg)', width: '100%', maxWidth: '500px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0 }}>{modal.isEdit ? 'Cập nhật chuyên khoa' : 'Thêm chuyên khoa'}</h3>
                            <button onClick={() => setModal({ isOpen: false, isEdit: false, data: {} })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>
                        
                        {formError && <div style={{ color: 'var(--c-danger)', background: 'var(--c-danger-bg)', padding: '10px', borderRadius: '6px', marginBottom: '16px', fontSize: '0.9rem' }}>{formError}</div>}

                        <form onSubmit={handleFormSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            <div className="form-group">
                                <label className="form-label">Mã chuyên khoa (*)</label>
                                <input 
                                    type="text" 
                                    className="form-input" 
                                    required 
                                    value={modal.data.specialtyCode || ''} 
                                    onChange={e => setModal({ ...modal, data: { ...modal.data, specialtyCode: e.target.value } })} 
                                    disabled={modal.isEdit}
                                    placeholder="VD: CARDIO"
                                    style={{ backgroundColor: modal.isEdit ? 'var(--c-bg)' : 'white' }}
                                />
                            </div>
                            <div className="form-group">
                                <label className="form-label">Tên chuyên khoa (*)</label>
                                <input 
                                    type="text" 
                                    className="form-input" 
                                    required 
                                    value={modal.data.name || ''} 
                                    onChange={e => setModal({ ...modal, data: { ...modal.data, name: e.target.value } })} 
                                    placeholder="VD: Tim mạch"
                                />
                            </div>
                            <div className="form-group">
                                <label className="form-label">Mô tả</label>
                                <textarea 
                                    className="form-textarea" 
                                    rows={3}
                                    value={modal.data.description || ''} 
                                    onChange={e => setModal({ ...modal, data: { ...modal.data, description: e.target.value } })} 
                                />
                            </div>
                            <div className="form-group">
                                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 500, cursor: 'pointer' }}>
                                    <input 
                                        type="checkbox" 
                                        checked={modal.data.isActive ?? true}
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, isActive: e.target.checked } })}
                                        style={{ width: '18px', height: '18px' }}
                                    />
                                    Đang hoạt động
                                </label>
                                {!modal.data.isActive && modal.isEdit && (
                                    <div style={{ color: 'var(--c-warning)', fontSize: '0.85rem', marginTop: '8px', display: 'flex', gap: '4px', alignItems: 'flex-start' }}>
                                        <AlertTriangle size={14} style={{ flexShrink: 0, marginTop: '2px' }}/>
                                        Ngừng hoạt động sẽ làm chuyên khoa bị ẩn khỏi form đặt lịch mới.
                                    </div>
                                )}
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px' }}>
                                <button type="button" className="btn-secondary" onClick={() => setModal({ isOpen: false, isEdit: false, data: {} })}>Hủy</button>
                                <button type="submit" className="btn-primary" disabled={formLoading}>
                                    {formLoading ? 'Đang lưu...' : 'Xác nhận lưu'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
