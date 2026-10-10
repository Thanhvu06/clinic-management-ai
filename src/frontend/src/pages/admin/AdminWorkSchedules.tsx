import { toLocalDateString } from '../../utils/formatters';
import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { CalendarDays, Plus, Clock, Edit, CheckCircle, XCircle, PlayCircle } from 'lucide-react';
import { Alert, Button, DatePicker, Form, Input, Modal, Select, Space, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import { PageHeader, FilterBar, DataTable, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminWorkSchedules.module.css';
import { useDialog } from '../../contexts/DialogContext';

interface WorkSchedule {
    id: number;
    doctorId: number;
    workDate: string;
    startTime: string;
    endTime: string;
    isActive: boolean;
    slots?: any[]; // The backend doesn't return slots in WorkScheduleDto currently.
}

// Date-only API values represent a local calendar day, not a UTC timestamp.
const parseWorkDate = (value: string) => {
    const [year, month, day] = value.split('-').map(Number);
    return new Date(year, month - 1, day);
};

export const AdminWorkSchedules: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [doctors, setDoctors] = useState<any[]>([]);
    const [selectedDoctorId, setSelectedDoctorId] = useState<number | ''>('');
    const [schedules, setSchedules] = useState<WorkSchedule[]>([]);
    const [loading, setLoading] = useState(false);
    const [loadError, setLoadError] = useState('');
    const [doctorsError, setDoctorsError] = useState('');
    const [doctorsLoading, setDoctorsLoading] = useState(true);

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, isEdit: boolean, data: Partial<WorkSchedule> }>({ isOpen: false, isEdit: false, data: {} });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');
    const [actionLoading, setActionLoading] = useState<number | null>(null);

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

    const fetchSchedules = async () => {
        setLoading(true);
        setLoadError('');
        try {
            const res = await axiosClient.get<any, ApiResponse<WorkSchedule[]>>(`/admin/doctors/${selectedDoctorId}/work-schedules`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải lịch làm việc.');
            if (res.success && res.data) {
                // Sort by date and time
                const sorted = [...res.data].sort((a, b) => {
                    const dateDiff = parseWorkDate(a.workDate).getTime() - parseWorkDate(b.workDate).getTime();
                    if (dateDiff !== 0) return dateDiff;
                    return a.startTime.localeCompare(b.startTime);
                });
                setSchedules(sorted);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải lịch làm việc.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        if (!selectedDoctorId) {
            setSchedules([]);
            return;
        }
        fetchSchedules();
    }, [selectedDoctorId]);

    const handleFormSubmit = async () => {
        setFormError('');

        if (!modal.data.workDate) {
            setFormError('Vui lòng chọn ngày làm việc.');
            return;
        }

        // Inputs return HH:mm; unchanged API values can still include seconds.
        const startTime = modal.data.startTime?.padEnd(8, ':00');
        const endTime = modal.data.endTime?.padEnd(8, ':00');
        if (startTime && endTime && startTime >= endTime) {
            setFormError('Giờ bắt đầu phải trước giờ kết thúc.');
            return;
        }

        setFormLoading(true);
        try {
            if (modal.isEdit) {
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/work-schedules/${modal.data.id}`, {
                    workDate: modal.data.workDate,
                    startTime: modal.data.startTime,
                    endTime: modal.data.endTime
                });
                if (res.success) {
                    showAlert('Cập nhật lịch làm việc thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchSchedules();
                }
            } else {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/admin/doctors/${selectedDoctorId}/work-schedules`, {
                    workDate: modal.data.workDate,
                    startTime: modal.data.startTime,
                    endTime: modal.data.endTime
                });
                if (res.success) {
                    showAlert('Tạo lịch làm việc thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchSchedules();
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra.');
        } finally {
            setFormLoading(false);
        }
    };

    const handleToggleStatus = async (id: number, currentStatus: boolean) => {
        showConfirm(`Bạn có chắc chắn muốn ${currentStatus ? 'khóa' : 'mở khóa'} lịch làm việc này?`, async () => {
            try {
                const res = await axiosClient.patch<any, ApiResponse<any>>(`/admin/work-schedules/${id}/status`, {
                    isActive: !currentStatus
                });
                if (res.success) {
                    fetchSchedules();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            }
        });
    };

    const handleGenerateSlots = async (id: number) => {
        showConfirm('Sinh ca khám (slots) cho lịch này? Lịch cũ có thể bị ảnh hưởng nếu đã được đặt.', async () => {
            setActionLoading(id);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/admin/work-schedules/${id}/generate-slots`, {});
                if (res.success) {
                    showAlert('Sinh slot thành công.', 'Thành công', 'success');
                    fetchSchedules();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra khi sinh slot.', 'Lỗi', 'error');
            } finally {
                setActionLoading(null);
            }
        });
    };

    const formatDate = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(parseWorkDate(dateString));
        } catch {
            return dateString;
        }
    };

    const getDayOfWeek = (dateString: string) => {
        try {
            const days = ['Chủ nhật', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'];
            return days[parseWorkDate(dateString).getDay()];
        } catch {
            return '';
        }
    };

    const closeModal = () => { if (!formLoading) setModal({ isOpen: false, isEdit: false, data: {} }); };

    return (
        <div className={styles.schedulesPage}>
            <PageHeader title="Quản lý lịch làm việc" actions={selectedDoctorId ?
                <Button type="primary" icon={<Plus size={18} />} onClick={() => { setFormError(''); setModal({ isOpen: true, isEdit: false, data: { workDate: toLocalDateString() } }); }}>Thêm lịch làm việc</Button> : undefined} />
            <FilterBar>
                <label className={styles.scheduleDoctorLabel} htmlFor="scheduleDoctor">Chọn Bác sĩ:</label>
                <Select id="scheduleDoctor" className={styles.scheduleDoctorFilter} showSearch={{ optionFilterProp: 'label' }} allowClear
                    placeholder="-- Vui lòng chọn bác sĩ --" value={selectedDoctorId || undefined} loading={doctorsLoading}
                    onChange={value => { setSelectedDoctorId(value ?? ''); setLoadError(''); setSchedules([]); }}
                    options={doctors.map(doctor => ({ value: doctor.id, label: doctor.fullName }))} />
            </FilterBar>
            {doctorsLoading ? <LoadingState /> : doctorsError ? <InlineError message={doctorsError} onRetry={fetchDoctors} />
                : !selectedDoctorId ? <EmptyState icon={<CalendarDays size={48} />} title="Chưa chọn bác sĩ" description="Vui lòng chọn một bác sĩ từ danh sách để xem lịch làm việc." />
                : loading ? <LoadingState message="Đang tải lịch làm việc..." />
                : loadError ? <InlineError message={loadError} onRetry={fetchSchedules} />
                : schedules.length === 0 ? <EmptyState title="Bác sĩ chưa có lịch làm việc nào." /> : <>
                    <Alert className={styles.scheduleSlotHint} type="info" title="Lưu ý: Bạn cần sinh slot để bệnh nhân có thể đặt lịch." showIcon />
                    <div className={styles.scheduleTable}>
                        <DataTable data={schedules} keyExtractor={schedule => schedule.id} columns={[
                            { header: 'Ngày', accessor: schedule => <><Typography.Text strong>{formatDate(schedule.workDate)}</Typography.Text><div className={styles.scheduleDay}>{getDayOfWeek(schedule.workDate)}</div></> },
                            { header: 'Ca làm việc', accessor: schedule => <div className={styles.scheduleTime}><Clock size={16} /><span>{schedule.startTime.substring(0, 5)} - {schedule.endTime.substring(0, 5)}</span></div> },
                            { header: 'Trạng thái', accessor: schedule => <Tag color={schedule.isActive ? 'success' : 'error'}>{schedule.isActive ? 'Đang hoạt động' : 'Đã khóa'}</Tag> },
                            { header: 'Thao tác', align: 'right', accessor: schedule => <Space wrap>
                                <Button size="small" type="primary" icon={<PlayCircle size={14} />} loading={actionLoading === schedule.id} disabled={actionLoading === schedule.id}
                                    title="Tự động chia thời gian thành các khung giờ khám (slots)" onClick={() => handleGenerateSlots(schedule.id)}>Sinh Slot</Button>
                                <Button size="small" icon={<Edit size={14} />} onClick={() => { setFormError(''); setModal({ isOpen: true, isEdit: true, data: { ...schedule } }); }}>Sửa</Button>
                                <Button size="small" danger={schedule.isActive} icon={schedule.isActive ? <XCircle size={14} /> : <CheckCircle size={14} />}
                                    onClick={() => handleToggleStatus(schedule.id, schedule.isActive)}>{schedule.isActive ? 'Khóa' : 'Mở khóa'}</Button>
                            </Space> }
                        ]} />
                    </div>
                </>}
            <Modal title={modal.isEdit ? 'Cập nhật lịch làm việc' : 'Thêm lịch làm việc'} open={modal.isOpen} onCancel={closeModal}
                footer={null} destroyOnHidden maskClosable={!formLoading} closable={!formLoading} keyboard={!formLoading}>
                {formError && <Alert className={styles.scheduleFormError} type="error" title={formError} showIcon />}
                <Form layout="vertical" onFinish={handleFormSubmit}>
                    <Form.Item label="Ngày làm việc (*)" htmlFor="scheduleWorkDate">
                        <DatePicker id="scheduleWorkDate" className={styles.scheduleDatePicker} format="YYYY-MM-DD" value={modal.data.workDate ? dayjs(modal.data.workDate) : null}
                            onChange={value => setModal({ ...modal, data: { ...modal.data, workDate: value?.format('YYYY-MM-DD') || '' } })} inputReadOnly />
                    </Form.Item>
                    <div className={styles.scheduleTimeFields}>
                        <Form.Item label="Từ giờ (*)" htmlFor="scheduleStartTime"><Input id="scheduleStartTime" type="time" required value={modal.data.startTime?.substring(0, 5) || ''}
                            onChange={e => setModal({ ...modal, data: { ...modal.data, startTime: e.target.value } })} /></Form.Item>
                        <Form.Item label="Đến giờ (*)" htmlFor="scheduleEndTime"><Input id="scheduleEndTime" type="time" required value={modal.data.endTime?.substring(0, 5) || ''}
                            onChange={e => setModal({ ...modal, data: { ...modal.data, endTime: e.target.value } })} /></Form.Item>
                    </div>
                    <div className={styles.scheduleFormActions}>
                        <Button disabled={formLoading} onClick={closeModal}>Hủy</Button>
                        <Button type="primary" htmlType="submit" loading={formLoading}>{formLoading ? 'Đang lưu...' : 'Xác nhận lưu'}</Button>
                    </div>
                </Form>
            </Modal>
        </div>
    );
};
