import React, { useState, useEffect } from 'react';
import { Button, Card, DatePicker, Form, Input, Modal, Select } from 'antd';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { CalendarCheck, Plus, XCircle, Clock, RefreshCw } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
import {
    PageHeader, DataTable, StatusBadge, LoadingState, EmptyState, Pagination
} from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './DoctorLeaveRequests.module.css';

interface LeaveRequest {
    id: number;
    startDateTime: string;
    endDateTime: string;
    reason: string;
    status: string;
    adminNote: string | null;
}

const statusLabels: Record<string, string> = {
    Pending: 'Chờ duyệt',
    Approved: 'Đã duyệt',
    Rejected: 'Đã từ chối',
    Cancelled: 'Đã rút'
};

export const DoctorLeaveRequests: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [requests, setRequests] = useState<LeaveRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [statusFilter, setStatusFilter] = useState('');

    const [modalOpen, setModalOpen] = useState(false);
    const [formLoading, setFormLoading] = useState(false);
    const [form] = Form.useForm();

    const fetchRequests = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/leave-requests?${params.toString()}`);
            if (res.success && res.data) {
                setRequests(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error handling
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, [page, statusFilter]);

    const handleCreate = async (values: { startDate: any; endDate: any; reason: string }) => {
        const startDate = values.startDate.toISOString();
        const endDate = values.endDate.toISOString();

        if (new Date(startDate) >= new Date(endDate)) {
            showAlert('Thời gian bắt đầu phải trước thời gian kết thúc.', 'Lỗi', 'error');
            return;
        }
        if (new Date(startDate) < new Date()) {
            showAlert('Không thể xin nghỉ trong quá khứ.', 'Lỗi', 'error');
            return;
        }

        setFormLoading(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>('/doctor/leave-requests', {
                startDateTime: startDate,
                endDateTime: endDate,
                reason: values.reason
            });
            if (res.success) {
                showAlert('Tạo yêu cầu nghỉ thành công.', 'Thành công', 'success');
                setModalOpen(false);
                form.resetFields();
                fetchRequests();
            }
        } catch (error: any) {
            showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
        } finally {
            setFormLoading(false);
        }
    };

    const handleWithdraw = async (id: number) => {
        showConfirm('Bạn có chắc chắn muốn rút lại yêu cầu nghỉ này?', async () => {
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/leave-requests/${id}/withdraw`, {});
                if (res.success) {
                    showAlert('Đã rút yêu cầu nghỉ.', 'Thành công', 'success');
                    fetchRequests();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            }
        });
    };

    const formatDateTime = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit'
            }).format(new Date(dateString));
        } catch {
            return dateString;
        }
    };

    const columns: DataTableColumn<LeaveRequest>[] = [
        {
            header: 'Thời gian nghỉ',
            accessor: (req) => (
                <div>
                    <div className={styles.leaveTime}>
                        <Clock size={14} style={{ marginRight: 6, color: 'var(--cc-color-teal)', verticalAlign: -2 }} />
                        Từ: {formatDateTime(req.startDateTime)}
                    </div>
                    <div className={styles.leaveTimeMuted}>Đến: {formatDateTime(req.endDateTime)}</div>
                </div>
            )
        },
        { header: 'Lý do', accessor: (req) => <div className={styles.reasonCell}>{req.reason}</div> },
        { header: 'Ghi chú quản trị', accessor: (req) => <div className={styles.adminNoteCell}>{req.adminNote || '-'}</div> },
        { header: 'Trạng thái', accessor: (req) => <StatusBadge status={req.status} label={statusLabels[req.status] || req.status} /> },
        {
            header: 'Thao tác',
            align: 'right',
            accessor: (req) => req.status === 'Pending' ? (
                <Button size="small" danger icon={<XCircle size={14} />} onClick={() => handleWithdraw(req.id)}>
                    Rút Y/C
                </Button>
            ) : null
        }
    ];

    return (
        <div>
            <PageHeader
                title="Yêu cầu nghỉ"
                actions={
                    <>
                        <Button icon={<RefreshCw size={14} />} onClick={() => fetchRequests()}>
                            Làm mới
                        </Button>
                        <Button type="primary" icon={<Plus size={16} />} onClick={() => setModalOpen(true)}>
                            Tạo yêu cầu mới
                        </Button>
                    </>
                }
            />

            <Card size="small" className={styles.filterCard}>
                <Select
                    style={{ width: 250 }}
                    value={statusFilter || undefined}
                    placeholder="Tất cả trạng thái"
                    allowClear
                    onChange={(val) => { setStatusFilter(val || ''); setPage(1); }}
                    options={[
                        { value: 'Pending', label: 'Chờ duyệt' },
                        { value: 'Approved', label: 'Đã duyệt' },
                        { value: 'Rejected', label: 'Đã từ chối' },
                        { value: 'Cancelled', label: 'Đã rút' }
                    ]}
                />
            </Card>

            {loading ? (
                <LoadingState message="Đang tải dữ liệu..." />
            ) : requests.length === 0 ? (
                <EmptyState icon={<CalendarCheck size={44} />} title="Chưa có yêu cầu nghỉ nào." />
            ) : (
                <DataTable columns={columns} data={requests} keyExtractor={(req) => req.id} />
            )}

            <Pagination page={page} totalPages={Math.max(1, Math.ceil(totalItems / 10))} totalRecords={totalItems} onPageChange={setPage} />

            <Modal
                title="Tạo yêu cầu nghỉ phép"
                open={modalOpen}
                onCancel={() => setModalOpen(false)}
                footer={null}
                destroyOnHidden
            >
                <Form form={form} layout="vertical" onFinish={handleCreate}>
                    <Form.Item
                        label="Lưu ý"
                    >
                        <div style={{ padding: 12, background: 'var(--cc-color-info-bg)', color: 'var(--cc-color-info)', borderRadius: 6, fontSize: '0.9rem' }}>
                            Yêu cầu nghỉ sẽ cần được Quản trị viên duyệt. Vui lòng nộp yêu cầu trước ít nhất 1-2 ngày để tránh ảnh hưởng lịch bệnh nhân.
                        </div>
                    </Form.Item>
                    <Form.Item name="startDate" label="Từ thời gian" rules={[{ required: true, message: 'Vui lòng chọn thời gian bắt đầu' }]}>
                        <DatePicker showTime style={{ width: '100%' }} format="DD/MM/YYYY HH:mm" />
                    </Form.Item>
                    <Form.Item name="endDate" label="Đến thời gian" rules={[{ required: true, message: 'Vui lòng chọn thời gian kết thúc' }]}>
                        <DatePicker showTime style={{ width: '100%' }} format="DD/MM/YYYY HH:mm" />
                    </Form.Item>
                    <Form.Item name="reason" label="Lý do nghỉ" rules={[{ required: true, message: 'Vui lòng nêu rõ lý do' }]}>
                        <Input.TextArea rows={3} placeholder="Nêu rõ lý do..." />
                    </Form.Item>
                    <Form.Item style={{ textAlign: 'right', marginBottom: 0 }}>
                        <Button onClick={() => setModalOpen(false)} style={{ marginRight: 8 }}>Hủy</Button>
                        <Button type="primary" htmlType="submit" loading={formLoading}>Gửi yêu cầu</Button>
                    </Form.Item>
                </Form>
            </Modal>
        </div>
    );
};
