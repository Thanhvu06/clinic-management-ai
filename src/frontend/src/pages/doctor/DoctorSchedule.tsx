import { toLocalDateString } from '../../utils/formatters';
import React, { useState, useEffect, useCallback } from 'react';
import { Button, Card, DatePicker, Flex, Form, Input, Modal } from 'antd';
import {
    ChevronLeft, ChevronRight, Clock,
    CalendarPlus, AlertTriangle, RefreshCw, ShieldAlert
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorScheduleDayDto, LeavePreviewDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, InlineError, EmptyState, LoadingState } from '../../components/common';
import styles from './DoctorSchedule.module.css';

export const DoctorSchedule: React.FC = () => {
    const { showAlert, showToast } = useDialog();

    // Compute week start (Monday) and week end (Sunday)
    const getMonday = (d: Date) => {
        const date = new Date(d);
        const day = date.getDay();
        const diff = date.getDate() - day + (day === 0 ? -6 : 1);
        return new Date(date.setDate(diff));
    };

    const [currentMonday, setCurrentMonday] = useState<Date>(() => getMonday(new Date()));
    const [scheduleDays, setScheduleDays] = useState<DoctorScheduleDayDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState<string | null>(null);

    // Leave request modal state
    const [isLeaveModalOpen, setIsLeaveModalOpen] = useState(false);
    const [leaveForm] = Form.useForm();
    const [leaveStartDate, setLeaveStartDate] = useState('');
    const [leaveEndDate, setLeaveEndDate] = useState('');
    const [leavePreview, setLeavePreview] = useState<LeavePreviewDto | null>(null);
    const [previewLoading, setPreviewLoading] = useState(false);
    const [submittingLeave, setSubmittingLeave] = useState(false);

    const startDateStr = toLocalDateString(currentMonday);
    const sunday = new Date(currentMonday);
    sunday.setDate(sunday.getDate() + 6);
    const endDateStr = toLocalDateString(sunday);

    const loadSchedule = useCallback(async () => {
        setLoading(true);
        setLoadError(null);
        try {
            const res = await doctorApi.getSchedule(startDateStr, endDateStr);
            if (res.success && res.data) {
                setScheduleDays(res.data);
            }
        } catch (err: any) {
            setLoadError(err.response?.data?.message || err.message || 'Không thể tải lịch trực của bác sĩ.');
        } finally {
            setLoading(false);
        }
    }, [startDateStr, endDateStr]);

    useEffect(() => {
        loadSchedule();
    }, [loadSchedule]);

    const handlePrevWeek = () => {
        const d = new Date(currentMonday);
        d.setDate(d.getDate() - 7);
        setCurrentMonday(d);
    };

    const handleNextWeek = () => {
        const d = new Date(currentMonday);
        d.setDate(d.getDate() + 7);
        setCurrentMonday(d);
    };

    const handleTodayWeek = () => {
        setCurrentMonday(getMonday(new Date()));
    };

    // When leave dates change, check preview
    useEffect(() => {
        if (!leaveStartDate || !leaveEndDate) {
            setLeavePreview(null);
            return;
        }

        if (leaveStartDate > leaveEndDate) {
            setLeavePreview(null);
            return;
        }

        const fetchPreview = async () => {
            setPreviewLoading(true);
            try {
                const res = await doctorApi.previewLeave(leaveStartDate, leaveEndDate);
                if (res.success && res.data) {
                    setLeavePreview(res.data);
                }
            } catch (err) {
                setLeavePreview(null);
            } finally {
                setPreviewLoading(false);
            }
        };

        const timer = setTimeout(fetchPreview, 400);
        return () => clearTimeout(timer);
    }, [leaveStartDate, leaveEndDate]);

    const handleLeaveValuesChange = (_changed: any, all: any) => {
        setLeaveStartDate(all.startDate ? all.startDate.format('YYYY-MM-DD') : '');
        setLeaveEndDate(all.endDate ? all.endDate.format('YYYY-MM-DD') : '');
    };

    const closeLeaveModal = () => {
        setIsLeaveModalOpen(false);
        leaveForm.resetFields();
        setLeaveStartDate('');
        setLeaveEndDate('');
        setLeavePreview(null);
    };

    const handleSubmitLeave = async (values: { startDate: any; endDate: any; reason: string }) => {
        setSubmittingLeave(true);
        try {
            const res = await doctorApi.createLeaveRequest({
                startDate: values.startDate.format('YYYY-MM-DD'),
                endDate: values.endDate.format('YYYY-MM-DD'),
                reason: values.reason.trim()
            });

            if (res.success) {
                showToast('Đã gửi đơn xin nghỉ phép thành công! Ban quản lý sẽ xem xét.', 'success');
                closeLeaveModal();
                await loadSchedule();
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể gửi đơn xin nghỉ phép.', 'Lỗi', 'error');
        } finally {
            setSubmittingLeave(false);
        }
    };

    return (
        <div>
            <PageHeader
                title="Lịch trực & Ca khám tuần"
                subtitle="Xem lịch trực được phân bổ, danh sách các khung giờ và thông tin bệnh nhân đã đặt."
                actions={
                    <Flex gap="small" align="center" wrap>
                        <Button danger type="primary" icon={<CalendarPlus size={15} />} onClick={() => setIsLeaveModalOpen(true)}>
                            Đăng ký nghỉ phép
                        </Button>
                        <Flex align="center" style={{ border: '1px solid var(--cc-color-border)', borderRadius: 8, overflow: 'hidden' }}>
                            <Button type="text" onClick={handlePrevWeek} title="Tuần trước" icon={<ChevronLeft size={18} />} />
                            <Button type="text" onClick={handleTodayWeek}>Hôm nay</Button>
                            <Button type="text" onClick={handleNextWeek} title="Tuần sau" icon={<ChevronRight size={18} />} />
                        </Flex>
                    </Flex>
                }
            />

            {loadError && (
                <InlineError message={loadError} onRetry={loadSchedule} />
            )}

            <Card className={styles.weekBar}>
                <Flex justify="space-between" align="center">
                    <span className={styles.weekLabel}>
                        Tuần từ {currentMonday.toLocaleDateString('vi-VN')} đến {sunday.toLocaleDateString('vi-VN')}
                    </span>
                    <Button type="link" onClick={loadSchedule} disabled={loading} icon={<RefreshCw size={14} className={loading ? 'animate-spin' : ''} />}>
                        Làm mới
                    </Button>
                </Flex>
            </Card>

            {loading ? (
                <LoadingState message="Đang tải lịch trực..." height="200px" />
            ) : scheduleDays.length === 0 ? (
                <EmptyState
                    title="Không có lịch làm việc cho tuần này"
                    description="Hiện chưa có ca trực nào được phân bổ cho khoảng thời gian này."
                />
            ) : (
                <div>
                    {scheduleDays.map(day => (
                        <Card key={day.date} className={styles.dayCard}>
                            <Flex justify="space-between" align="center" className={styles.dayHeader}>
                                <div>
                                    <span className={styles.dayName}>{day.dayOfWeekName}</span>
                                    <span className={styles.dayDate}>({day.date})</span>
                                </div>
                                <span className={day.shifts.length > 0 ? styles.shiftCountActive : styles.shiftCountIdle}>
                                    {day.shifts.length > 0 ? `${day.shifts.length} ca trực` : 'Nghỉ trực'}
                                </span>
                            </Flex>

                            {day.shifts.length === 0 ? (
                                <div className={styles.noShift}>
                                    Không có ca làm việc nào được phân công.
                                </div>
                            ) : (
                                <div>
                                    {day.shifts.map(shift => (
                                        <div key={shift.workScheduleId} className={styles.shiftBox}>
                                            <Flex justify="space-between" align="center" wrap gap="small" className={styles.shiftTitleRow}>
                                                <Flex align="center" gap="small">
                                                    <Clock size={16} style={{ color: 'var(--cc-color-info)' }} />
                                                    <span className={styles.shiftName}>
                                                        {shift.shiftName}: {shift.startTime.substring(0, 5)} - {shift.endTime.substring(0, 5)}
                                                    </span>
                                                    <span className={styles.roomTag}>{shift.room}</span>
                                                </Flex>
                                                <span className={styles.slotCount}>
                                                    {shift.slots.filter(s => s.isBooked).length}/{shift.slots.length} khung giờ đã được đặt
                                                </span>
                                            </Flex>

                                            <div className={styles.slotsGrid}>
                                                {shift.slots.map(slot => (
                                                    <div key={slot.id} className={`${styles.slotBox} ${slot.isBooked ? styles.slotBoxBooked : ''}`}>
                                                        <div className={`${styles.slotTimeRow} ${slot.isBooked ? styles.slotTimeRowBooked : ''}`}>
                                                            <span>{slot.startTime.substring(0, 5)} - {slot.endTime.substring(0, 5)}</span>
                                                            <span className={slot.isBooked ? styles.slotStatusBooked : styles.slotStatusFree}>
                                                                {slot.isBooked ? 'Đã đặt' : 'Trống'}
                                                            </span>
                                                        </div>
                                                        {slot.isBooked && (
                                                            <div className={styles.slotPatient}>
                                                                <div className={styles.slotPatientName}>{slot.patientName}</div>
                                                                <div className={styles.slotPatientPhone}>{slot.patientPhone}</div>
                                                            </div>
                                                        )}
                                                    </div>
                                                ))}
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </Card>
                    ))}
                </div>
            )}

            <Modal
                title={
                    <Flex align="center" gap={8}>
                        <CalendarPlus size={20} style={{ color: 'var(--cc-color-danger)' }} />
                        <span>Đăng ký nghỉ phép</span>
                    </Flex>
                }
                open={isLeaveModalOpen}
                onCancel={closeLeaveModal}
                footer={null}
                destroyOnHidden
            >
                <Form form={leaveForm} layout="vertical" onFinish={handleSubmitLeave} onValuesChange={handleLeaveValuesChange}>
                    <Flex gap="small">
                        <Form.Item name="startDate" label="Từ ngày" rules={[{ required: true, message: 'Vui lòng chọn từ ngày' }]} style={{ flex: 1 }}>
                            <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
                        </Form.Item>
                        <Form.Item name="endDate" label="Đến ngày" rules={[{ required: true, message: 'Vui lòng chọn đến ngày' }]} style={{ flex: 1 }}>
                            <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
                        </Form.Item>
                    </Flex>

                    <Form.Item name="reason" label="Lý do nghỉ phép" rules={[{ required: true, message: 'Vui lòng nêu rõ lý do' }]}>
                        <Input.TextArea rows={3} placeholder="Ghi rõ lý do xin nghỉ để ban quản lý xem xét..." />
                    </Form.Item>

                    {previewLoading && (
                        <Flex align="center" justify="center" gap={6} style={{ fontSize: '0.85rem', color: 'var(--cc-color-text-muted)', padding: '10px 0' }}>
                            <RefreshCw size={14} className="animate-spin" />
                            <span>Đang kiểm tra lịch hẹn bị ảnh hưởng...</span>
                        </Flex>
                    )}

                    {leavePreview && (
                        <div className={`${styles.previewBanner} ${leavePreview.affectedAppointmentCount > 0 ? styles.previewBannerWarn : styles.previewBannerOk}`}>
                            <Flex align="center" gap={8} style={{ fontWeight: 600, fontSize: '0.9rem' }} className={leavePreview.affectedAppointmentCount > 0 ? styles.previewHeadWarn : styles.previewHeadOk}>
                                {leavePreview.affectedAppointmentCount > 0 ? <AlertTriangle size={18} /> : <ShieldAlert size={18} />}
                                <span>
                                    {leavePreview.affectedAppointmentCount > 0
                                        ? `Có ${leavePreview.affectedAppointmentCount} lịch hẹn của bệnh nhân bị ảnh hưởng!`
                                        : 'Không có lịch hẹn nào của bệnh nhân bị ảnh hưởng.'}
                                </span>
                            </Flex>
                            {leavePreview.affectedAppointmentCount > 0 && (
                                <div className={styles.previewList}>
                                    <ul style={{ margin: 0, paddingLeft: 16 }}>
                                        {leavePreview.affectedAppointments.map(a => (
                                            <li key={a.id}>
                                                {a.appointmentDate} ({a.startTime.substring(0, 5)} - {a.endTime.substring(0, 5)}): {a.patientName} ({a.patientPhone})
                                            </li>
                                        ))}
                                    </ul>
                                    <div className={styles.previewFootnote}>
                                        * Nếu được duyệt, bộ phận lễ tân sẽ chủ động liên hệ bệnh nhân để sắp xếp lại ca khám.
                                    </div>
                                </div>
                            )}
                        </div>
                    )}

                    <Form.Item style={{ textAlign: 'right', marginBottom: 0, marginTop: 16 }}>
                        <Button onClick={closeLeaveModal} style={{ marginRight: 8 }}>Hủy</Button>
                        <Button danger type="primary" htmlType="submit" loading={submittingLeave}>
                            Gửi đơn nghỉ phép
                        </Button>
                    </Form.Item>
                </Form>
            </Modal>
        </div>
    );
};
