import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { User, CheckCircle, XCircle, Eye } from 'lucide-react';
import { Alert, Button, Descriptions, Form, Input, Modal, Select, Space } from 'antd';
import { PageHeader, FilterBar, DataTable, Pagination, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminLeaves.module.css';
import { useDialog } from '../../contexts/DialogContext';

interface LeaveRequest {
    id: number;
    doctorId: number;
    doctorName: string;
    startDateTime: string;
    endDateTime: string;
    reason: string;
    status: string;
    adminNote: string | null;
}

export const AdminLeaves: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [requests, setRequests] = useState<LeaveRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState('');
    const [doctorsError, setDoctorsError] = useState('');
    const [doctorsLoading, setDoctorsLoading] = useState(true);
    const [formError, setFormError] = useState('');
    const [formErrorType, setFormErrorType] = useState<'warning' | 'error'>('error');
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    
    // Filters
    const [statusFilter, setStatusFilter] = useState('');
    const [doctors, setDoctors] = useState<any[]>([]);
    const [doctorIdFilter, setDoctorIdFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, req: LeaveRequest | null }>({ isOpen: false, req: null });
    const [adminNote, setAdminNote] = useState('');
    const [actionLoading, setActionLoading] = useState(false);

    const fetchDoctors = async () => {
        setDoctorsLoading(true);
        setDoctorsError('');
        try {
            const res = await axiosClient.get<any, ApiResponse<any>>('/admin/doctors?isActive=true&pageSize=100');
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh sách bác sĩ.');
            if (res.success) setDoctors(res.data.items);
        } catch (error) {
            setDoctorsError((error as Error)?.message || 'Không thể tải danh sách bác sĩ.');
        } finally {
            setDoctorsLoading(false);
        }
    };
    useEffect(() => {
        fetchDoctors();
    }, []);

    const fetchRequests = async () => {
        setLoading(true);
        setLoadError('');
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (statusFilter) params.append('status', statusFilter);
            if (doctorIdFilter) params.append('doctorId', doctorIdFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/leave-requests?${params.toString()}`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải yêu cầu nghỉ.');
            if (res.success && res.data) {
                setRequests(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải yêu cầu nghỉ.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, [page, statusFilter, doctorIdFilter]);

    const handleProcess = async (action: 'approve' | 'reject') => {
        if (!modal.req) return;
        
        showConfirm(`Bạn có chắc chắn muốn ${action === 'approve' ? 'duyệt' : 'từ chối'} yêu cầu này?`, async () => {
            setActionLoading(true);
            setFormError('');
            setFormErrorType('error');
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/admin/leave-requests/${modal.req!.id}/${action}`, {
                    adminNote
                });
                if (res.success) {
                    showAlert(action === 'approve' ? 'Đã duyệt yêu cầu nghỉ.' : 'Đã từ chối yêu cầu nghỉ.', 'Thành công', 'success');
                    setModal({ isOpen: false, req: null });
                    fetchRequests();
                }
            } catch (error: any) {
                if (error?.errorCode === 'LEAVE_HAS_AFFECTED_APPOINTMENTS') {
                    setFormError('Bác sĩ đang có lịch hẹn bị ảnh hưởng. Hãy để lễ tân xử lý các lịch này trước khi duyệt nghỉ.');
                    setFormErrorType('warning');
                } else {
                    setFormError(error?.message || 'Có lỗi xảy ra.');
                    setFormErrorType('error');
                }
            } finally {
                setActionLoading(false);
            }
        });
    };

    const formatDate = (dateString: string) => {
        try {
            const date = new Date(dateString);
            return new Intl.DateTimeFormat('vi-VN', { 
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit'
            }).format(date);
        } catch {
            return dateString;
        }
    };

    const getStatusBadge = (status: string) => {
        switch(status) {
            case 'Pending': return <StatusBadge status={status} label="Chờ xử lý" />;
            case 'Approved': return <StatusBadge status={status} label="Đã duyệt" />;
            case 'Rejected': return <StatusBadge status={status} label="Đã từ chối" />;
            case 'Cancelled': return <StatusBadge status={status} label="Đã hủy" />;
            default: return <StatusBadge status={status} />;
        }
    };

    const closeModal = () => { if (!actionLoading) setModal({ isOpen: false, req: null }); };

    return (
        <div className={styles.leavesPage}>
            <PageHeader title="Quản lý yêu cầu nghỉ" />
            <FilterBar>
                <Select className={styles.leaveDoctorFilter} aria-label="Bác sĩ" value={doctorIdFilter} loading={doctorsLoading} disabled={!!doctorsError}
                    onChange={value => { setDoctorIdFilter(value); setPage(1); }}
                    options={[{ value: '', label: 'Tất cả bác sĩ' }, ...doctors.map(doctor => ({ value: String(doctor.id), label: doctor.fullName }))]} />
                <Select className={styles.leaveStatusFilter} aria-label="Trạng thái" value={statusFilter}
                    onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                        { value: '', label: 'Tất cả trạng thái' }, { value: 'Pending', label: 'Chờ xử lý' },
                        { value: 'Approved', label: 'Đã duyệt' }, { value: 'Rejected', label: 'Đã từ chối' }, { value: 'Cancelled', label: 'Đã hủy' }
                    ]} />
                {doctorsError && <Alert className={styles.leaveDoctorWarning} type="warning" title={`Danh sách bác sĩ: ${doctorsError}`} showIcon action={<Button size="small" aria-label="Thử lại danh sách bác sĩ" onClick={fetchDoctors}>Thử lại</Button>} />}
            </FilterBar>
            {loading ? <LoadingState /> : loadError ? <InlineError message={loadError} onRetry={fetchRequests} />
                : requests.length === 0 ? <EmptyState title="Không tìm thấy yêu cầu nào." /> : <>
                    <div className={styles.leaveTable}>
                        <DataTable data={requests} keyExtractor={request => request.id} columns={[
                            { header: 'Bác sĩ', accessor: request => <div className={styles.leaveDoctor}><User size={16} />{request.doctorName}</div> },
                            { header: 'Thời gian nghỉ', accessor: request => <><div>Từ: {formatDate(request.startDateTime)}</div><div>Đến: {formatDate(request.endDateTime)}</div></> },
                            { header: 'Trạng thái', accessor: request => getStatusBadge(request.status) },
                            { header: 'Thao tác', align: 'right', accessor: request => <Button size="small" icon={<Eye size={14} />} onClick={() => { setModal({ isOpen: true, req: request }); setAdminNote(''); setFormError(''); }}>Chi tiết</Button> }
                        ]} />
                    </div>
                    <Pagination page={page} totalPages={Math.ceil(totalItems / 10)} totalRecords={totalItems} onPageChange={setPage} />
                </>}
            {!loading && !loadError && <div className={styles.leaveTotal}>Tổng cộng: {totalItems} yêu cầu</div>}
            <Modal title="Chi tiết yêu cầu nghỉ" open={modal.isOpen} onCancel={closeModal} footer={null} destroyOnHidden
                maskClosable={!actionLoading} closable={!actionLoading} keyboard={!actionLoading}>
                {modal.req && <>
                    {formError && <Alert className={styles.leaveFormError} type={formErrorType} title={formError} showIcon />}
                    <Descriptions column={1} items={[
                        { key: 'doctor', label: 'Bác sĩ', children: <strong>{modal.req.doctorName}</strong> },
                        { key: 'time', label: 'Thời gian', children: <strong>{formatDate(modal.req.startDateTime)} - {formatDate(modal.req.endDateTime)}</strong> },
                        { key: 'status', label: 'Trạng thái', children: getStatusBadge(modal.req.status) }
                    ]} />
                    <div className={styles.leaveReasonBox}><span className={styles.leaveNoteLabel}>Lý do xin nghỉ:</span>{modal.req.reason}</div>
                    {modal.req.adminNote && <div className={styles.leaveAdminNoteBox}><span className={styles.leaveNoteLabel}>Ghi chú quản trị:</span>{modal.req.adminNote}</div>}
                    {modal.req.status === 'Pending' ? <>
                        <Form layout="vertical">
                            <Form.Item label="Thêm ghi chú xử lý (Tùy chọn)" htmlFor="leaveAdminNote">
                                <Input.TextArea id="leaveAdminNote" rows={2} value={adminNote} onChange={e => setAdminNote(e.target.value)} placeholder="Ghi chú thêm về quyết định duyệt/từ chối..." />
                            </Form.Item>
                        </Form>
                        <Space className={styles.leaveActions}>
                            <Button danger icon={<XCircle size={18} />} loading={actionLoading} disabled={actionLoading} onClick={() => handleProcess('reject')}>Từ chối</Button>
                            <Button type="primary" icon={<CheckCircle size={18} />} loading={actionLoading} disabled={actionLoading} onClick={() => handleProcess('approve')}>Duyệt yêu cầu</Button>
                        </Space>
                    </> : <div className={styles.leaveActions}><Button disabled={actionLoading} onClick={closeModal}>Đóng</Button></div>}
                </>}
            </Modal>
        </div>
    );
};
