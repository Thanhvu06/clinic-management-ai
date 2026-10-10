import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Plus } from 'lucide-react';
import { Alert, Button, Form, Input, Modal, Select, Tag, Typography } from 'antd';
import { PageHeader, FilterBar, DataTable, Pagination, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminUsers.module.css';
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
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState('');

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
        setLoadError('');
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: pageSize.toString()
            });
            if (search) params.append('search', search);
            if (roleFilter) params.append('role', roleFilter);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/users?${params.toString()}`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải tài khoản.');
            if (res.success && res.data) {
                setUsers(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải tài khoản.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchUsers();
    }, [page, roleFilter, statusFilter]);

    const handleSearchSubmit = () => {
        if (page !== 1) setPage(1);
        else fetchUsers();
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

    const handleCreateSubmit = async () => {
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
            case 'Pharmacist': return 'Dược sĩ';
            case 'DiagnosticTechnician': return 'Kỹ thuật viên';
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

    const roleColors: Record<string, string> = {
        Admin: 'error', Doctor: 'processing', Receptionist: 'default', Patient: 'default',
        Pharmacist: 'green', DiagnosticTechnician: 'purple'
    };
    const roleOptions = ['Admin', 'Doctor', 'Receptionist', 'Patient', 'Pharmacist', 'DiagnosticTechnician']
        .map(role => ({ value: role, label: translateRole(role) }));

    return (
        <div className={styles.usersPage}>
            <PageHeader title="Quản lý tài khoản" actions={
                <Button type="primary" icon={<Plus size={18} />} onClick={() => { setFormError(''); setIsCreateModalOpen(true); }}>
                    Tạo tài khoản nhân sự
                </Button>
            } />
            <FilterBar>
                <Input.Search className={styles.userSearch} placeholder="Tìm theo tên, email, sđt..."
                    value={search} onChange={e => setSearch(e.target.value)} onSearch={handleSearchSubmit} enterButton="Lọc" />
                <Select className={styles.userFilter} aria-label="Vai trò" value={roleFilter}
                    onChange={value => { setRoleFilter(value); setPage(1); }}
                    options={[{ value: '', label: 'Tất cả vai trò' }, ...roleOptions]} />
                <Select className={styles.userFilter} aria-label="Trạng thái" value={statusFilter}
                    onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                        { value: '', label: 'Tất cả trạng thái' }, { value: 'true', label: 'Đang hoạt động' }, { value: 'false', label: 'Đã khóa' }
                    ]} />
            </FilterBar>
            {loading ? <LoadingState /> : loadError ? <InlineError message={loadError} onRetry={fetchUsers} />
                : users.length === 0 ? <EmptyState title="Không tìm thấy tài khoản nào phù hợp." /> : <>
                    <div className={styles.userTable}>
                        <DataTable data={users} keyExtractor={user => user.id} columns={[
                            { header: 'Họ và tên', accessor: user => <><Typography.Text strong>{user.fullName}</Typography.Text><div className={styles.userJoined}>Tham gia: {formatDate(user.createdAt)}</div></> },
                            { header: 'Thông tin liên hệ', accessor: user => <div className={styles.userContact}><div>{user.email}</div><Typography.Text type="secondary">{user.phoneNumber}</Typography.Text></div> },
                            { header: 'Vai trò', accessor: user => <div className={styles.userRoles}>{user.roles.map(role => <Tag key={role} color={roleColors[role] || 'default'}>{translateRole(role)}</Tag>)}</div> },
                            { header: 'Trạng thái', accessor: user => <Tag color={user.isActive ? 'success' : 'error'}>{user.isActive ? 'Đang hoạt động' : 'Đã khóa'}</Tag> },
                            { header: 'Thao tác', align: 'right', accessor: user => <Button size="small" danger={user.isActive} type={user.isActive ? 'default' : 'primary'}
                                onClick={() => toggleStatus(user.id, user.isActive)} disabled={user.roles.includes('Admin')}
                                title={user.roles.includes('Admin') ? 'Không thể đổi trạng thái Admin' : ''}>{user.isActive ? 'Khóa' : 'Mở khóa'}</Button> }
                        ]} />
                    </div>
                    <Pagination page={page} totalPages={Math.ceil(totalItems / pageSize)} totalRecords={totalItems} onPageChange={setPage} />
                </>}
            {!loading && !loadError && <div className={styles.userTotal}>Tổng cộng: {totalItems} tài khoản</div>}
            <Modal title="Tạo tài khoản nhân sự" open={isCreateModalOpen} footer={null} destroyOnHidden
                onCancel={() => { if (!formLoading) setIsCreateModalOpen(false); }} maskClosable={!formLoading} closable={!formLoading} keyboard={!formLoading}>
                {formError && <Alert className={styles.userFormError} type="error" title={formError} showIcon />}
                <Form layout="vertical" onFinish={handleCreateSubmit}>
                    <Form.Item label="Họ và tên (*)" htmlFor="userFullName"><Input id="userFullName" required value={formData.fullName} onChange={e => setFormData({ ...formData, fullName: e.target.value })} placeholder="Nguyễn Văn A" /></Form.Item>
                    <Form.Item label="Email (*)" htmlFor="userEmail"><Input id="userEmail" type="email" required value={formData.email} onChange={e => setFormData({ ...formData, email: e.target.value })} placeholder="email@example.com" /></Form.Item>
                    <Form.Item label="Số điện thoại (*)" htmlFor="userPhone"><Input id="userPhone" required value={formData.phoneNumber} onChange={e => setFormData({ ...formData, phoneNumber: e.target.value })} placeholder="09xxxxxxxxx" /></Form.Item>
                    <Form.Item label="Mật khẩu (*)" htmlFor="userPassword"><Input.Password id="userPassword" required minLength={8} value={formData.password} onChange={e => setFormData({ ...formData, password: e.target.value })} placeholder="Tối thiểu 8 ký tự" /></Form.Item>
                    <Form.Item label="Vai trò (*)" htmlFor="userRole"><Select id="userRole" value={formData.role} onChange={role => setFormData({ ...formData, role })} options={roleOptions.filter(option => option.value !== 'Patient')} /></Form.Item>
                    {formData.role === 'Doctor' && <Alert className={styles.userRoleNotice} type="warning" title={'Lưu ý: Sau khi tạo tài khoản, bạn cần vào menu "Bác sĩ" để tạo hồ sơ và phân công chuyên khoa.'} />}
                    <div className={styles.userFormActions}>
                        <Button disabled={formLoading} onClick={() => setIsCreateModalOpen(false)}>Hủy</Button>
                        <Button type="primary" htmlType="submit" loading={formLoading}>{formLoading ? 'Đang tạo...' : 'Xác nhận tạo'}</Button>
                    </div>
                </Form>
            </Modal>
        </div>
    );
};
