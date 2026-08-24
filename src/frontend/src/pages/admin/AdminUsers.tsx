import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Users, Search, Plus, CheckCircle, XCircle } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface UserDto {
    id: string;
    email: string;
    phoneNumber: string;
    fullName: string;
    isActive: boolean;
    roles: string[];
    createdAt: string;
}

export const AdminUsers: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [users, setUsers] = useState<UserDto[]>([]);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const pageSize = 10;
    const [loading, setLoading] = useState(false);

    // Filters
    const [search, setSearch] = useState('');
    const [roleFilter, setRoleFilter] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Create Modal
    const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
    const [formData, setFormData] = useState({
        email: '',
        phoneNumber: '',
        fullName: '',
        password: '',
        role: 'Receptionist'
    });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');

    const fetchUsers = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: pageSize.toString()
            });
            if (search) params.append('search', search);
            if (roleFilter) params.append('role', roleFilter);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/users?${params.toString()}`);
            if (res.success && res.data) {
                setUsers(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchUsers();
    }, [page, roleFilter, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchUsers();
    };

    const toggleStatus = async (userId: string, currentStatus: boolean) => {
        const actionName = currentStatus ? 'khóa' : 'mở khóa';
        showConfirm(`Bạn có chắc chắn muốn ${actionName} tài khoản này?`, async () => {
            try {
                const res = await axiosClient.patch<any, ApiResponse<any>>(`/admin/users/${userId}/status`, {
                    isActive: !currentStatus
                });
                if (res.success) {
                    showAlert(`Đã ${actionName} tài khoản thành công.`, 'Thành công', 'success');
                    fetchUsers();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            }
        });
    };

    const handleCreateSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');
        setFormLoading(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>('/admin/users', formData);
            if (res.success) {
                showAlert('Tạo tài khoản nhân sự thành công.', 'Thành công', 'success');
                setIsCreateModalOpen(false);
                setFormData({ email: '', phoneNumber: '', fullName: '', password: '', role: 'Receptionist' });
                fetchUsers();
            }
        } catch (error: any) {
            setFormError(error?.message || 'Không thể tạo tài khoản.');
        } finally {
            setFormLoading(false);
        }
    };

    const translateRole = (r: string) => {
        switch(r) {
            case 'Admin': return 'Quản trị viên';
            case 'Doctor': return 'Bác sĩ';
            case 'Receptionist': return 'Lễ tân';
            case 'Patient': return 'Bệnh nhân';
            default: return r;
        }
    };

    const formatDate = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN').format(new Date(dateString));
        } catch {
            return dateString;
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <Users size={24} /> Quản lý tài khoản
                </h2>
                <button className="btn-primary" onClick={() => setIsCreateModalOpen(true)}>
                    <Plus size={18} /> Tạo tài khoản nhân sự
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
                                placeholder="Tìm theo tên, email, sđt..." 
                                style={{ paddingLeft: '40px' }}
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </div>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={roleFilter} onChange={e => { setRoleFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả vai trò</option>
                            <option value="Admin">Quản trị viên</option>
                            <option value="Doctor">Bác sĩ</option>
                            <option value="Receptionist">Lễ tân</option>
                            <option value="Patient">Bệnh nhân</option>
                        </select>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="true">Đang hoạt động</option>
                            <option value="false">Đã khóa</option>
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
                                <th>Họ và tên</th>
                                <th>Thông tin liên hệ</th>
                                <th>Vai trò</th>
                                <th>Trạng thái</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {users.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Không tìm thấy tài khoản nào phù hợp.
                                    </td>
                                </tr>
                            ) : users.map(u => (
                                <tr key={u.id}>
                                    <td>
                                        <div style={{ fontWeight: 500 }}>{u.fullName}</div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>Tham gia: {formatDate(u.createdAt)}</div>
                                    </td>
                                    <td>
                                        <div>{u.email}</div>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-muted)' }}>{u.phoneNumber}</div>
                                    </td>
                                    <td>
                                        <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                                            {u.roles.map(r => (
                                                <span key={r} className={`badge ${r === 'Admin' ? 'badge-danger' : r === 'Doctor' ? 'badge-info' : 'badge-muted'}`}>
                                                    {translateRole(r)}
                                                </span>
                                            ))}
                                        </div>
                                    </td>
                                    <td>
                                        {u.isActive ? 
                                            <span className="badge badge-success"><CheckCircle size={14} style={{ marginRight: '4px' }}/> Đang hoạt động</span> : 
                                            <span className="badge badge-danger"><XCircle size={14} style={{ marginRight: '4px' }}/> Đã khóa</span>
                                        }
                                    </td>
                                    <td style={{ textAlign: 'right' }}>
                                        <button 
                                            className={u.isActive ? "btn-danger" : "btn-primary"} 
                                            style={{ padding: '6px 12px', fontSize: '0.85rem' }}
                                            onClick={() => toggleStatus(u.id, u.isActive)}
                                            disabled={u.roles.includes('Admin')}
                                            title={u.roles.includes('Admin') ? 'Không thể đổi trạng thái Admin' : ''}
                                        >
                                            {u.isActive ? 'Khóa' : 'Mở khóa'}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>
            
            <div style={{ marginTop: '16px', color: 'var(--c-muted)', fontSize: '0.9rem' }}>
                Tổng cộng: {totalItems} tài khoản
            </div>

            {/* Create Modal */}
            {isCreateModalOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: 'var(--radius-lg)', width: '100%', maxWidth: '500px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0 }}>Tạo tài khoản nhân sự</h3>
                            <button onClick={() => setIsCreateModalOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><XCircle size={24} color="var(--c-muted)"/></button>
                        </div>
                        
                        {formError && <div style={{ color: 'var(--c-danger)', background: 'var(--c-danger-bg)', padding: '10px', borderRadius: '6px', marginBottom: '16px', fontSize: '0.9rem' }}>{formError}</div>}

                        <form onSubmit={handleCreateSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            <div className="form-group">
                                <label className="form-label">Họ và tên (*)</label>
                                <input type="text" className="form-input" required value={formData.fullName} onChange={e => setFormData({...formData, fullName: e.target.value})} placeholder="Nguyễn Văn A" />
                            </div>
                            <div className="form-group">
                                <label className="form-label">Email (*)</label>
                                <input type="email" className="form-input" required value={formData.email} onChange={e => setFormData({...formData, email: e.target.value})} placeholder="email@example.com" />
                            </div>
                            <div className="form-group">
                                <label className="form-label">Số điện thoại (*)</label>
                                <input type="text" className="form-input" required value={formData.phoneNumber} onChange={e => setFormData({...formData, phoneNumber: e.target.value})} placeholder="09xxxxxxxxx" />
                            </div>
                            <div className="form-group">
                                <label className="form-label">Mật khẩu (*)</label>
                                <input type="password" className="form-input" required minLength={8} value={formData.password} onChange={e => setFormData({...formData, password: e.target.value})} placeholder="Tối thiểu 8 ký tự" />
                            </div>
                            <div className="form-group">
                                <label className="form-label">Vai trò (*)</label>
                                <select className="form-select" value={formData.role} onChange={e => setFormData({...formData, role: e.target.value})}>
                                    <option value="Receptionist">Lễ tân</option>
                                    <option value="Doctor">Bác sĩ</option>
                                    <option value="Admin">Quản trị viên</option>
                                </select>
                                {formData.role === 'Doctor' && (
                                    <div style={{ fontSize: '0.85rem', color: 'var(--c-warning)', marginTop: '4px' }}>
                                        Lưu ý: Sau khi tạo tài khoản, bạn cần vào menu "Bác sĩ" để tạo hồ sơ và phân công chuyên khoa.
                                    </div>
                                )}
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px' }}>
                                <button type="button" className="btn-secondary" onClick={() => setIsCreateModalOpen(false)}>Hủy</button>
                                <button type="submit" className="btn-primary" disabled={formLoading}>
                                    {formLoading ? 'Đang tạo...' : 'Xác nhận tạo'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};

