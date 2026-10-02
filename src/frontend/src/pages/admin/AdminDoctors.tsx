import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Plus, XCircle, Edit, Stethoscope, Briefcase, Award, X } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
import { formatDoctorName } from '../../utils/doctorNameHelper';

interface DoctorSpecialty {
    specialtyId: number;
    specialtyName: string;
    isPrimary: boolean;
}

interface Doctor {
    id: number;
    userId: string;
    fullName: string;
    email: string;
    academicTitle?: string;
    experienceYears: number;
    description: string;
    isActive: boolean;
    specialties: DoctorSpecialty[];
}

export const AdminDoctors: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    
    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, isEdit: boolean, data: Partial<Doctor> }>({ isOpen: false, isEdit: false, data: {} });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');
    
    // Data for Create Form
    const [unassignedUsers, setUnassignedUsers] = useState<any[]>([]);
    const [allSpecialties, setAllSpecialties] = useState<any[]>([]);

    const fetchDoctors = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/doctors?${params.toString()}`);
            if (res.success && res.data) {
                setDoctors(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchDoctors();
    }, [page, statusFilter]);

    const loadFormDependencies = async () => {
        try {
            const [usersRes, specsRes] = await Promise.all([
                axiosClient.get<any, ApiResponse<any>>('/admin/users?role=Doctor&isActive=true&pageSize=100'),
                axiosClient.get<any, ApiResponse<any>>('/admin/specialties?isActive=true&pageSize=100')
            ]);
            if (usersRes.success) setUnassignedUsers(usersRes.data.items);
            if (specsRes.success) setAllSpecialties(specsRes.data.items);
        } catch (e) {}
    };

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchDoctors();
    };

    const handleFormSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');
        setFormLoading(true);
        
        try {
            if (modal.isEdit) {
                // Update specialties
                if (modal.data.specialties && modal.data.specialties.length > 0) {
                    const primaryCount = modal.data.specialties.filter(s => s.isPrimary).length;
                    if (primaryCount !== 1) {
                        showAlert('Phải có chính xác 1 chuyên khoa chính.', 'Lỗi', 'error');
                        setFormLoading(false);
                        return;
                    }
                    await axiosClient.put<any, ApiResponse<any>>(`/admin/doctors/${modal.data.id}/specialties`, modal.data.specialties.map(s => ({
                        specialtyId: s.specialtyId,
                        isPrimary: s.isPrimary
                    })));
                }

                // Update basic info
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/doctors/${modal.data.id}`, {
                    academicTitle: modal.data.academicTitle,
                    experienceYears: Number(modal.data.experienceYears || 0),
                    description: modal.data.description,
                    isActive: modal.data.isActive
                });
                
                if (res.success) {
                    showAlert('Cập nhật bác sĩ thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchDoctors();
                }
            } else {
                if (!modal.data.userId) {
                    setFormError('Vui lòng chọn tài khoản liên kết.');
                    setFormLoading(false);
                    return;
                }
                if (!modal.data.specialties || modal.data.specialties.length === 0) {
                    setFormError('Vui lòng chọn ít nhất 1 chuyên khoa.');
                    setFormLoading(false);
                    return;
                }
                const primaryCount = modal.data.specialties.filter(s => s.isPrimary).length;
                if (primaryCount !== 1) {
                    setFormError('Phải có chính xác 1 chuyên khoa chính.');
                    setFormLoading(false);
                    return;
                }

                const res = await axiosClient.post<any, ApiResponse<any>>('/admin/doctors', {
                    userId: modal.data.userId,
                    academicTitle: modal.data.academicTitle,
                    experienceYears: Number(modal.data.experienceYears || 0),
                    description: modal.data.description,
                    isActive: modal.data.isActive ?? true,
                    specialties: modal.data.specialties.map((s: any) => ({
                        specialtyId: s.specialtyId,
                        isPrimary: s.isPrimary
                    }))
                });
                if (res.success) {
                    showAlert('Thêm hồ sơ bác sĩ thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchDoctors();
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra.');
        } finally {
            setFormLoading(false);
        }
    };

    const openCreateModal = async () => {
        await loadFormDependencies();
        setModal({ isOpen: true, isEdit: false, data: { isActive: true, specialties: [] } });
    };

    const openEditModal = async (doc: Doctor) => {
        await loadFormDependencies();
        setModal({ isOpen: true, isEdit: true, data: { ...doc, specialties: [...doc.specialties] } });
    };

    const handleAddSpecialty = (specId: number) => {
        if (!specId) return;
        const current = modal.data.specialties || [];
        if (current.find(s => s.specialtyId === specId)) return;
        
        const specName = allSpecialties.find(s => s.id === specId)?.name || '';
        const isPrimary = current.length === 0; // First one is primary by default

        setModal({
            ...modal,
            data: {
                ...modal.data,
                specialties: [...current, { specialtyId: specId, specialtyName: specName, isPrimary }]
            }
        });
    };

    const handleRemoveSpecialty = (specId: number) => {
        const current = modal.data.specialties || [];
        if (current.length === 1) {
            showAlert('Bác sĩ phải có ít nhất 1 chuyên khoa.', 'Lỗi', 'error');
            return;
        }
        
        const isRemovingPrimary = current.find(s => s.specialtyId === specId)?.isPrimary;
        const newSpecs = current.filter(s => s.specialtyId !== specId);
        
        if (isRemovingPrimary && newSpecs.length > 0) {
            newSpecs[0].isPrimary = true;
        }

        setModal({
            ...modal,
            data: {
                ...modal.data,
                specialties: newSpecs
            }
        });
    };

    const handleSetPrimarySpecialty = (specId: number) => {
        const current = modal.data.specialties || [];
        showConfirm('Đổi chuyên khoa chính có thể ảnh hưởng đến lịch hẹn hiện có. Bạn có chắc chắn muốn tiếp tục?', () => {
            const newSpecs = current.map(s => ({
                ...s,
                isPrimary: s.specialtyId === specId
            }));
    
            setModal({
                ...modal,
                data: {
                    ...modal.data,
                    specialties: newSpecs
                }
            });
        });
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <Stethoscope size={24} /> Quản lý bác sĩ
                </h2>
                <button className="btn-primary" onClick={openCreateModal}>
                    <Plus size={18} /> Thêm hồ sơ bác sĩ
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
                                placeholder="Tìm theo tên..." 
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
                                <th>Bác sĩ</th>
                                <th>Chuyên môn</th>
                                <th>Trạng thái</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {doctors.length === 0 ? (
                                <tr>
                                    <td colSpan={4} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Không tìm thấy dữ liệu.
                                    </td>
                                </tr>
                            ) : doctors.map(d => (
                                <tr key={d.id}>
                                    <td>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                            <div style={{ width: '40px', height: '40px', borderRadius: '50%', background: 'var(--c-teal)', color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 600, fontSize: '1.2rem' }}>
                                                {d.fullName.charAt(0)}
                                            </div>
                                            <div>
                                                <div style={{ fontWeight: 600 }}>{formatDoctorName(d.academicTitle, d.fullName)}</div>
                                                <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>{d.email}</div>
                                            </div>
                                        </div>
                                    </td>
                                    <td>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.9rem', marginBottom: '4px' }}>
                                            <Briefcase size={14} color="var(--c-teal)"/> {d.experienceYears} năm kinh nghiệm
                                        </div>
                                        <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                                            {d.specialties.map(s => (
                                                <span key={s.specialtyId} className={`badge ${s.isPrimary ? 'badge-primary' : 'badge-muted'}`} title={s.isPrimary ? 'Chuyên khoa chính' : ''}>
                                                    {s.isPrimary && <Award size={10} style={{ marginRight: '2px' }} />}
                                                    {s.specialtyName}
                                                </span>
                                            ))}
                                        </div>
                                    </td>
                                    <td>
                                        {d.isActive ? 
                                            <span className="badge badge-success">Đang hoạt động</span> : 
                                            <span className="badge badge-danger">Ngừng hoạt động</span>
                                        }
                                    </td>
                                    <td style={{ textAlign: 'right' }}>
                                        <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.85rem' }} onClick={() => openEditModal(d)}>
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
                Tổng cộng: {totalItems} bác sĩ
            </div>

            {/* Form Modal */}
            {modal.isOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: 'var(--radius-lg)', width: '100%', maxWidth: '600px', maxHeight: '90vh', overflowY: 'auto' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0 }}>{modal.isEdit ? 'Cập nhật hồ sơ bác sĩ' : 'Thêm hồ sơ bác sĩ'}</h3>
                            <button onClick={() => setModal({ isOpen: false, isEdit: false, data: {} })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>
                        
                        {formError && <div style={{ color: 'var(--c-danger)', background: 'var(--c-danger-bg)', padding: '10px', borderRadius: '6px', marginBottom: '16px', fontSize: '0.9rem' }}>{formError}</div>}

                        <form onSubmit={handleFormSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            {!modal.isEdit && (
                                <div className="form-group">
                                    <label className="form-label">Chọn tài khoản User liên kết (*)</label>
                                    <select 
                                        className="form-select" 
                                        required 
                                        value={modal.data.userId || ''} 
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, userId: e.target.value } })}
                                    >
                                        <option value="">-- Chọn User (Role Doctor) --</option>
                                        {unassignedUsers.map(u => (
                                            <option key={u.id} value={u.id}>{u.fullName} ({u.email})</option>
                                        ))}
                                    </select>
                                </div>
                            )}

                            <div style={{ display: 'flex', gap: '16px' }}>
                                <div className="form-group" style={{ flex: 1 }}>
                                    <label className="form-label">Học hàm/Học vị</label>
                                    <input 
                                        type="text" 
                                        className="form-input" 
                                        value={modal.data.academicTitle || ''} 
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, academicTitle: e.target.value } })} 
                                        placeholder="VD: PGS. TS. BS"
                                    />
                                </div>
                                <div className="form-group" style={{ width: '150px' }}>
                                    <label className="form-label">Số năm K.Nghiệm</label>
                                    <input 
                                        type="number" 
                                        className="form-input" 
                                        required 
                                        min="0"
                                        max="100"
                                        value={modal.data.experienceYears || ''} 
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, experienceYears: parseInt(e.target.value) } })} 
                                    />
                                </div>
                            </div>

                            <div className="form-group">
                                <label className="form-label">Chuyên khoa (*)</label>
                                <div style={{ display: 'flex', gap: '8px', marginBottom: '8px' }}>
                                    <select id="specSelect" className="form-select" style={{ flex: 1 }}>
                                        <option value="">-- Chọn chuyên khoa để thêm --</option>
                                        {allSpecialties.filter(s => !(modal.data.specialties || []).find(ds => ds.specialtyId === s.id)).map(s => (
                                            <option key={s.id} value={s.id}>{s.name}</option>
                                        ))}
                                    </select>
                                    <button 
                                        type="button" 
                                        className="btn-secondary" 
                                        onClick={() => {
                                            const select = document.getElementById('specSelect') as HTMLSelectElement;
                                            if (select.value) {
                                                handleAddSpecialty(Number(select.value));
                                                select.value = '';
                                            }
                                        }}
                                    >
                                        Thêm
                                    </button>
                                </div>
                                <div style={{ border: '1px solid var(--c-border)', borderRadius: '6px', padding: '12px' }}>
                                    {(modal.data.specialties || []).length === 0 ? (
                                        <div style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Chưa chọn chuyên khoa nào.</div>
                                    ) : (
                                        (modal.data.specialties || []).map(s => (
                                            <div key={s.specialtyId} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 0', borderBottom: '1px dashed var(--c-bg)' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                    {s.isPrimary ? <Award size={16} color="var(--c-teal)"/> : <div style={{ width: '16px' }}/>}
                                                    <span style={{ fontWeight: s.isPrimary ? 600 : 400 }}>{s.specialtyName}</span>
                                                    {s.isPrimary && <span className="badge badge-info" style={{ fontSize: '0.7rem' }}>Chính</span>}
                                                </div>
                                                <div style={{ display: 'flex', gap: '8px' }}>
                                                    {!s.isPrimary && (
                                                        <button type="button" className="btn-secondary" style={{ padding: '2px 8px', fontSize: '0.8rem' }} onClick={() => handleSetPrimarySpecialty(s.specialtyId)}>
                                                            Đặt làm chính
                                                        </button>
                                                    )}
                                                    <button type="button" style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--c-danger)' }} onClick={() => handleRemoveSpecialty(s.specialtyId)}>
                                                        <XCircle size={16} />
                                                    </button>
                                                </div>
                                            </div>
                                        ))
                                    )}
                                </div>
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
