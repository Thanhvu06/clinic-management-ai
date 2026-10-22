import { getApiErrorMessage } from '../../utils/apiError';
import { toLocalDateString, formatDateOnly } from '../../utils/formatters';
import React, { useState, useEffect, useCallback } from 'react';
import { Alert, Button, DatePicker, Flex, Form, Input, Modal } from 'antd';
import type { Dayjs } from 'dayjs';
import {
    ChevronLeft, ChevronRight,
    CalendarPlus, AlertTriangle, RefreshCw, ShieldAlert
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorScheduleDayDto, LeavePreviewDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, InlineError, EmptyState, LoadingState } from '../../components/common';
import styles from './DoctorSchedule.module.css';
import { DoctorWeekSchedule } from './DoctorWeekSchedule';

const vietnamToday = () => {
    const parts = new Intl.DateTimeFormat('en-CA', {
        timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit'
    }).formatToParts(new Date());
    const part = (type: string) => parts.find(value => value.type === type)?.value;
    return `${part('year')}-${part('month')}-${part('day')}`;
};

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
    const [previewError, setPreviewError] = useState('');
    const [previewRetry, setPreviewRetry] = useState(0);
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
            setLoadError(getApiErrorMessage(err, 'Không thể tải lịch trực của bác sĩ.'));
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

    // Ignore an older response when the selected dates change or the modal closes.
    useEffect(() => {
        setLeavePreview(null);
        setPreviewError('');
        if (!leaveStartDate || !leaveEndDate || leaveStartDate > leaveEndDate) {
            setPreviewLoading(false);
            return;
        }
        let active = true;
        setPreviewLoading(true);
        const fetchPreview = async () => {
            try {
                const res = await doctorApi.previewLeave(leaveStartDate, leaveEndDate);
                if (!active) return;
                if (res.success && res.data) setLeavePreview(res.data);
                else setPreviewError(getApiErrorMessage(res, 'Vui lòng thử lại.'));
            } catch (error) {
                if (active) setPreviewError(getApiErrorMessage(error, 'Vui lòng thử lại.'));
            } finally {
                if (active) setPreviewLoading(false);
            }
        };
        const timer = setTimeout(fetchPreview, 400);
        return () => { active = false; clearTimeout(timer); };
    }, [leaveStartDate, leaveEndDate, previewRetry]);

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
        setPreviewError('');
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
            showAlert(getApiErrorMessage(err, 'Không thể gửi đơn xin nghỉ phép.'), 'Lỗi', 'error');
        } finally {
            setSubmittingLeave(false);
        }
    };

    return (
        <div>
            <PageHeader
                title="Lịch trực & ca khám"
                actions={
                    <Flex gap="small" align="center" wrap>
                        <Button icon={<CalendarPlus size={15} />} onClick={() => setIsLeaveModalOpen(true)}>
                            Đăng ký nghỉ phép
                        </Button>
                        <Flex align="center" className={styles.surface1}>
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

            <div className={styles.weekBar}>
                <span className={styles.weekLabel}>
                    Tuần từ {currentMonday.toLocaleDateString('vi-VN')} đến {sunday.toLocaleDateString('vi-VN')}
                </span>
                <Button type="link" onClick={loadSchedule} disabled={loading} icon={<RefreshCw size={14} />}>
                    {loading ? 'Đang tải...' : 'Làm mới'}
                </Button>
            </div>

            {loading ? (
                <LoadingState message="Đang tải lịch trực..." height="200px" />
            ) : scheduleDays.length === 0 ? (
                <EmptyState
                    title="Không có lịch làm việc cho tuần này"
                    description="Hiện chưa có ca trực nào được phân bổ cho khoảng thời gian này."
                />
            ) : (
                <DoctorWeekSchedule days={scheduleDays} />
            )}

            <Modal
                title={
                    <Flex align="center" gap={8}>
                        <CalendarPlus size={20} className={styles.icon2} />
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
                        <Form.Item name="startDate" label="Từ ngày" rules={[{ required: true, message: 'Vui lòng chọn từ ngày' }, {
                            validator: (_, value: Dayjs) => !value || value.format('YYYY-MM-DD') > vietnamToday() ? Promise.resolve() : Promise.reject(new Error('Vui lòng chọn từ ngày mai.'))
                        }]} className={styles.icon3}>
                            <DatePicker className={styles.control1} format="DD/MM/YYYY" disabledDate={date => date.format('YYYY-MM-DD') <= vietnamToday()} />
                        </Form.Item>
                        <Form.Item name="endDate" label="Đến ngày" dependencies={['startDate']} rules={[{ required: true, message: 'Vui lòng chọn đến ngày' }, {
                            validator: (_, value: Dayjs) => !value || (value.format('YYYY-MM-DD') > vietnamToday() && (!leaveStartDate || value.format('YYYY-MM-DD') >= leaveStartDate))
                                ? Promise.resolve() : Promise.reject(new Error('Ngày kết thúc phải từ ngày mai và không trước ngày bắt đầu.'))
                        }]} className={styles.icon3}>
                            <DatePicker className={styles.control1} format="DD/MM/YYYY" disabledDate={date => date.format('YYYY-MM-DD') <= vietnamToday() || Boolean(leaveStartDate && date.format('YYYY-MM-DD') < leaveStartDate)} />
                        </Form.Item>
                    </Flex>

                    <Form.Item name="reason" label="Lý do nghỉ phép" rules={[{ required: true, message: 'Vui lòng nêu rõ lý do' }]}>
                        <Input.TextArea rows={3} placeholder="Ghi rõ lý do xin nghỉ để ban quản lý xem xét..." />
                    </Form.Item>

                    {previewLoading && (
                        <Flex align="center" justify="center" gap={6} className={styles.text1}>
                            <RefreshCw size={14} />
                            <span>Đang kiểm tra lịch hẹn bị ảnh hưởng...</span>
                        </Flex>
                    )}

                    {previewError && !previewLoading && <Alert type="error" title="Không kiểm tra được lịch hẹn bị ảnh hưởng"
                        description={previewError} action={<Button disabled={previewLoading} onClick={() => setPreviewRetry(value => value + 1)}>Thử lại</Button>} />}

                    {!previewError && !previewLoading && leavePreview && (
                        <div className={`${styles.previewBanner} ${leavePreview.affectedAppointmentCount > 0 ? styles.previewBannerWarn : styles.previewBannerOk}`}>
                            <Flex align="center" gap={8} className={[leavePreview.affectedAppointmentCount > 0 ? styles.previewHeadWarn : styles.previewHeadOk, styles.text2].join(' ')}>
                                {leavePreview.affectedAppointmentCount > 0 ? <AlertTriangle size={18} /> : <ShieldAlert size={18} />}
                                <span>
                                    {leavePreview.affectedAppointmentCount > 0
                                        ? `Có ${leavePreview.affectedAppointmentCount} lịch hẹn của bệnh nhân bị ảnh hưởng!`
                                        : 'Không có lịch hẹn nào của bệnh nhân bị ảnh hưởng.'}
                                </span>
                            </Flex>
                            {leavePreview.affectedAppointmentCount > 0 && (
                                <div className={styles.previewList}>
                                    <ul className={styles.spacing1}>
                                        {leavePreview.affectedAppointments.map(a => (
                                            <li key={a.id}>
                                                {formatDateOnly(a.appointmentDate)} ({a.startTime.substring(0, 5)} - {a.endTime.substring(0, 5)}): {a.patientName} ({a.patientPhone})
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

                    <Form.Item className={styles.icon4}>
                        <Button onClick={closeLeaveModal} className={styles.button1}>Hủy</Button>
                        <Button danger type="primary" htmlType="submit" loading={submittingLeave}>
                            Gửi đơn nghỉ phép
                        </Button>
                    </Form.Item>
                </Form>
            </Modal>
        </div>
    );
};
