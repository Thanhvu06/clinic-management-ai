import { getAppointmentHistoryActionLabel } from '../../utils/appointmentHistoryLabels';
import { toLocalDateString } from '../../utils/formatters';
import React, { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Button, DatePicker, Flex, Form, Input, Modal, Select, Typography } from 'antd';
import { Search, Eye, XCircle, Clock, PlusCircle, RefreshCw, Send, Pill, Stethoscope } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, DataTable, StatusBadge, LoadingState, Pagination, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './DoctorAppointments.module.css';
import { getApiErrorMessage } from '../../utils/apiError';
import { getAppointmentStatusLabel } from '../../utils/appointmentStatusLabels';

interface DoctorAppointment {
    id: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    patientGender: string;
    patientDob: string | null;
    specialtyId: number;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string | null;
    status: string;
}

export const DoctorAppointments: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert, showConfirm } = useDialog();
    const [appointments, setAppointments] = useState<DoctorAppointment[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');
    const [dateFilter, setDateFilter] = useState(''); // 'today' or ''
    const [fetchError, setFetchError] = useState<string | null>(null);

    // Modals
    const [modal, setModal] = useState<{ isOpen: boolean, apt: DoctorAppointment | null, view: 'detail' | 'revisit' | 'noshow' }>({ isOpen: false, apt: null, view: 'detail' });
    const [actionLoading, setActionLoading] = useState(false);

    const [history, setHistory] = useState<any[]>([]);

    const [currentPrescription, setCurrentPrescription] = useState<any>(null);
    const [prescriptionLoading, setPrescriptionLoading] = useState(false);
    const [prescriptionError, setPrescriptionError] = useState('');
    const prescriptionRequest = useRef(0);

    // No Show Form
    const [noShowReason, setNoShowReason] = useState('');

    // Revisit Form
    const [revisitForm] = Form.useForm();

    const fetchAppointments = async () => {
        setLoading(true);
        setFetchError(null);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);
            if (dateFilter === 'today') {
                const todayStr = toLocalDateString();
                params.append('date', todayStr);
            }

            const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments?${params.toString()}`);
            if (res.success && res.data) {
                let items = res.data.items as DoctorAppointment[];
                if (dateFilter === 'today') {
                    const todayStr = toLocalDateString();
                    items = items.filter(i => i.appointmentDate.startsWith(todayStr));
                }

                items.sort((a, b) => {
                    const dateDiff = new Date(a.appointmentDate).getTime() - new Date(b.appointmentDate).getTime();
                    if (dateDiff !== 0) return dateDiff;
                    return a.startTime.localeCompare(b.startTime);
                });

                setAppointments(items);
                setTotalItems(res.data.totalItems);
            }
        } catch (err: any) {
            setFetchError(err.response?.data?.message || err.message || 'Lỗi tải danh sách lịch khám. Vui lòng thử lại.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchAppointments();
    }, [page, statusFilter, dateFilter]);

    const handleSearchSubmit = () => {
        setPage(1);
        fetchAppointments();
    };

    const loadPrescription = async (appointmentId: number) => {
        const request = ++prescriptionRequest.current;
        setPrescriptionLoading(true);
        setPrescriptionError('');
        setCurrentPrescription(null);
        try {
            const response = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments/${appointmentId}/prescription`);
            if (request !== prescriptionRequest.current) return;
            if (response.success) setCurrentPrescription(response.data ?? null);
            else setPrescriptionError(getApiErrorMessage(response, 'Không tải được đơn thuốc'));
        } catch (error) {
            if (request === prescriptionRequest.current)
                setPrescriptionError(getApiErrorMessage(error, 'Không tải được đơn thuốc'));
        } finally {
            if (request === prescriptionRequest.current) setPrescriptionLoading(false);
        }
    };

    const openModal = async (apt: DoctorAppointment, view: 'detail' | 'revisit' | 'noshow') => {
        setModal({ isOpen: true, apt, view });
        setNoShowReason('');
        revisitForm.resetFields();
        setCurrentPrescription(null);

        await loadPrescription(apt.id);

        if (view === 'detail') {
            try {
                const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments/${apt.id}/history`);
                if (res.success && res.data) {
                    setHistory(res.data);
                }
            } catch {
                setHistory([]);
            }
        }
    };

    const handleNoShow = async () => {
        if (!modal.apt) return;

        showConfirm('Xác nhận đánh dấu bệnh nhân vắng mặt? Hành động này sẽ khóa lịch khám và không thể hoàn tác.', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/noshow`, {
                    reason: noShowReason
                });
                if (res.success) {
                    showAlert('Đã đánh dấu vắng mặt.', 'Thành công', 'success');
                    setModal({ isOpen: false, apt: null, view: 'detail' });
                    fetchAppointments();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            } finally {
                setActionLoading(false);
            }
        });
    };

    const handleRevisit = async (values: { revisitDate: any; revisitNote?: string }) => {
        if (!modal.apt) return;

        showConfirm('Gửi đề xuất tái khám đến bệnh nhân?', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/revisit-requests`, {
                    suggestedDate: values.revisitDate.format('YYYY-MM-DD'),
                    note: values.revisitNote || ''
                });
                if (res.success) {
                    showAlert('Đề xuất tái khám đã gửi đến bệnh nhân.', 'Thành công', 'success');
                    setModal({ isOpen: false, apt: null, view: 'detail' });
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            } finally {
                setActionLoading(false);
            }
        });
    };

    const formatDate = (dateString: string) => {
        try { return new Intl.DateTimeFormat('vi-VN').format(new Date(dateString)); }
        catch { return dateString; }
    };

    const closeModal = () => {
        prescriptionRequest.current += 1;
        setModal({ isOpen: false, apt: null, view: 'detail' });
    };

    const columns: DataTableColumn<DoctorAppointment>[] = [
        {
            header: 'Giờ khám',
            accessor: (apt) => (
                <div>
                    <div className={styles.timeCell}><Clock size={14} className={styles.clockIcon} />{apt.startTime.substring(0, 5)}</div>
                    <div className={styles.dateCell}>{formatDate(apt.appointmentDate)}</div>
                    <div className={styles.codeCell}>#{apt.appointmentCode}</div>
                </div>
            )
        },
        {
            header: 'Bệnh nhân',
            accessor: (apt) => (
                <div>
                    <div className={styles.mediumText}>{apt.patientName}</div>
                    <div className={styles.patientMeta}>{apt.patientGender} | {apt.patientDob ? formatDate(apt.patientDob) : 'N/A'}</div>
                </div>
            )
        },
        {
            header: 'Lý do khám',
            accessor: (apt) => <div className={styles.reasonCell}>{apt.reason || <Typography.Text type="secondary">Không có ghi chú</Typography.Text>}</div>
        },
        { header: 'Trạng thái', accessor: (apt) => <StatusBadge status={apt.status} label={getAppointmentStatusLabel(apt.status)} /> },
        {
            header: 'Thao tác',
            align: 'right',
            accessor: (apt) => (
                <Flex gap={8} justify="flex-end" wrap>
                    <Button size="small" icon={<Eye size={14} />} onClick={() => navigate(`/doctor/appointments/${apt.id}`)}>
                        Hồ sơ
                    </Button>
                    {apt.status === 'InConsultation' && (
                        <Button type="primary" size="small" icon={<Stethoscope size={14} />} onClick={() => navigate(`/doctor/appointments/${apt.id}/examination`)}>
                            Tiếp tục khám
                        </Button>
                    )}
                    {apt.status === 'CheckedIn' && (
                        <Button type="primary" size="small" icon={<Stethoscope size={14} />} onClick={() => navigate(`/doctor/appointments/${apt.id}/examination`)}>
                            Vào khám
                        </Button>
                    )}
                    {apt.status === 'Confirmed' && (
                        <Button type="primary" size="small" icon={<Eye size={14} />} onClick={() => openModal(apt, 'detail')}>
                            Chi tiết
                        </Button>
                    )}
                    {apt.status === 'Completed' && (
                        <Button size="small" icon={<PlusCircle size={14} />} onClick={() => openModal(apt, 'revisit')}>
                            Tái khám
                        </Button>
                    )}
                </Flex>
            )
        }
    ];

    return (
        <div>
            <PageHeader
                title="Danh sách lịch khám"
                actions={<Button icon={<RefreshCw size={14} />} onClick={() => fetchAppointments()}>Làm mới</Button>}
            />

            <div className={styles.filterCard}>
                <Input
                    className={styles.searchInput}
                    prefix={<Search size={16} className={styles.mutedIcon} />}
                    placeholder="Tìm theo mã lịch..."
                    value={search}
                    onChange={e => setSearch(e.target.value)}
                    onPressEnter={handleSearchSubmit}
                />
                <Select
                    className={styles.dateFilter}
                    value={dateFilter || undefined}
                    placeholder="Tất cả ngày"
                    allowClear
                    onChange={(val) => { setDateFilter(val || ''); setPage(1); }}
                    options={[{ value: 'today', label: 'Hôm nay' }]}
                />
                <Select
                    className={styles.statusFilter}
                    value={statusFilter || undefined}
                    placeholder="Tất cả trạng thái"
                    allowClear
                    onChange={(val) => { setStatusFilter(val || ''); setPage(1); }}
                    options={['Confirmed', 'CheckedIn', 'InConsultation', 'Completed', 'NoShow', 'Cancelled']
                        .map(status => ({ value: status, label: getAppointmentStatusLabel(status) }))}
                />
                <Button onClick={handleSearchSubmit}>Tìm</Button>
            </div>

            {fetchError && (
                <InlineError message={fetchError} onRetry={() => fetchAppointments()} />
            )}

            {loading ? (
                <LoadingState message="Đang tải dữ liệu..." />
            ) : (
                <DataTable columns={columns} data={appointments} keyExtractor={(apt) => apt.id} />
            )}

            <div className={styles.totalLine}>
                Tổng cộng: {totalItems} lịch khám {dateFilter === 'today' ? 'trong ngày hôm nay' : ''}
            </div>

            <Pagination page={page} totalPages={Math.max(1, Math.ceil(totalItems / 10))} totalRecords={totalItems} onPageChange={setPage} />

            <Modal
                title={
                    modal.view === 'detail' ? 'Chi tiết lịch khám' :
                    modal.view === 'noshow' ? 'Xác nhận vắng mặt' :
                    'Đề xuất tái khám'
                }
                open={modal.isOpen && !!modal.apt}
                onCancel={closeModal}
                footer={null}
                width={640}
                getContainer={false}
                destroyOnHidden
            >
                {modal.apt && (
                    <>
                        <Flex gap="middle" align="center" className={styles.patientInfoBar}>
                            <div className={styles.patientAvatar}>{modal.apt.patientName.charAt(0)}</div>
                            <div>
                                <div className={styles.modalPatientName}>{modal.apt.patientName}</div>
                                <div className={styles.patientMeta}>
                                    {modal.apt.patientGender} | Sinh: {modal.apt.patientDob ? formatDate(modal.apt.patientDob) : 'N/A'} | Lịch hẹn: {formatDate(modal.apt.appointmentDate)} ({modal.apt.startTime.substring(0, 5)})
                                </div>
                            </div>
                        </Flex>

                        {prescriptionLoading ? <LoadingState message="Đang tải đơn thuốc..." />
                            : prescriptionError ? <InlineError title="Không tải được đơn thuốc" message={prescriptionError} onRetry={() => void loadPrescription(modal.apt!.id)} /> : null}
                        {modal.view === 'detail' && (
                            <>
                                <div className={styles.detailBlock}>
                                    <div className={styles.infoBlockLabel}>Triệu chứng / lý do khám:</div>
                                    <div className={styles.infoBlockValue}>
                                        {modal.apt.reason || <Typography.Text type="secondary">Không có</Typography.Text>}
                                    </div>
                                </div>
                                <div className={styles.detailBlock}>
                                    <Flex justify="space-between" className={styles.statusRow}>
                                        <span className={styles.mediumText}>Trạng thái hiện tại</span>
                                        <StatusBadge status={modal.apt.status} label={getAppointmentStatusLabel(modal.apt.status)} />
                                    </Flex>
                                    {history.length > 0 && (
                                        <div className={styles.historyTimeline}>
                                            {history.map(h => (
                                                <div key={h.id} className={styles.historyItem}>
                                                    <div className={styles.historyDot}></div>
                                                    <div className={styles.historyAction}>{getAppointmentHistoryActionLabel(h.action)}</div>
                                                    {h.note && <div className={styles.historyNote}>{h.note}</div>}
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>
                                {modal.apt.status === 'Confirmed' && (
                                    <Flex vertical gap="small" className={styles.detailActions}>
                                        <div className={`${styles.infoBanner} ${styles.infoBannerInfo}`}>
                                            Bệnh nhân cần được tiếp nhận tại hàng đợi khám trước khi bắt đầu khám.
                                        </div>
                                        <Flex justify="flex-end" gap="small" wrap>
                                            <Button danger icon={<XCircle size={16} />} onClick={() => setModal({ ...modal, view: 'noshow' })}>
                                                Đánh dấu vắng mặt
                                            </Button>
                                            <Button type="primary" onClick={() => navigate('/doctor/queue')}>
                                                Mở hàng đợi
                                            </Button>
                                        </Flex>
                                    </Flex>
                                )}

                                {currentPrescription && (
                                    <div className={styles.prescriptionSection}>
                                        <Flex justify="space-between" align="center" className={styles.prescriptionHeader}>
                                            <Flex align="center" gap={8}>
                                                <Pill size={18} className={styles.primaryIcon} />
                                                <h4 className={styles.prescriptionTitle}>Đơn thuốc đã kê</h4>
                                            </Flex>
                                            <StatusBadge
                                                status={currentPrescription.status}
                                                label={currentPrescription.status === 'Dispensed' ? 'Đã cấp thuốc' : 'Chờ cấp thuốc'}
                                            />
                                        </Flex>
                                        {currentPrescription.notes && (
                                            <div className={styles.prescriptionNotes}>
                                                Ghi chú: {currentPrescription.notes}
                                            </div>
                                        )}
                                        <DataTable
                                            columns={[
                                                { header: 'Tên thuốc', accessor: (item: any) => <span className={styles.strongText}>{item.medicineName} ({item.medicineCode})</span> },
                                                { header: 'Số lượng', accessor: (item: any) => `${item.quantity} ${item.unit}` },
                                                { header: 'Liều dùng', accessor: 'dosage' },
                                                { header: 'Tần suất', accessor: 'frequency' },
                                                { header: 'Ghi chú', accessor: (item: any) => item.instructions || '-' }
                                            ]}
                                            data={currentPrescription.items || []}
                                            keyExtractor={(item: any, idx?: number) => item.medicineId ?? idx}
                                        />
                                    </div>
                                )}
                            </>
                        )}

                        {modal.view === 'noshow' && (
                            <Flex vertical gap="middle">
                                <div className={`${styles.infoBanner} ${styles.infoBannerWarning}`}>
                                    Hành động này sẽ hủy buổi khám vì bệnh nhân không đến. Vui lòng xác nhận chắc chắn vì không thể hoàn tác.
                                </div>
                                <div>
                                    <div className={styles.infoBlockLabel}>Ghi chú / lý do (tùy chọn)</div>
                                    <Input.TextArea
                                        rows={2}
                                        value={noShowReason}
                                        onChange={e => setNoShowReason(e.target.value)}
                                        placeholder="Ví dụ: Đã gọi điện 3 lần không bắt máy..."
                                    />
                                </div>
                                <Flex justify="flex-end" gap="small">
                                    <Button onClick={() => setModal({ ...modal, view: 'detail' })}>Hủy</Button>
                                    <Button danger type="primary" disabled={actionLoading} onClick={handleNoShow}>
                                        {actionLoading ? 'Đang xử lý...' : 'Xác nhận vắng mặt'}
                                    </Button>
                                </Flex>
                            </Flex>
                        )}

                        {modal.view === 'revisit' && (
                            <Form form={revisitForm} layout="vertical" onFinish={handleRevisit}>
                                <Form.Item name="revisitDate" label="Ngày hẹn tái khám đề nghị" rules={[{ required: true, message: 'Vui lòng chọn ngày tái khám' }]}>
                                    <DatePicker className={styles.control1} format="DD/MM/YYYY" />
                                </Form.Item>
                                <Form.Item name="revisitNote" label="Tin nhắn / lý do tái khám (tùy chọn)">
                                    <Input.TextArea rows={3} placeholder="Ví dụ: Tái khám sau khi uống hết thuốc..." />
                                </Form.Item>
                                <Flex justify="flex-end" gap="small">
                                    <Button onClick={() => setModal({ ...modal, view: 'detail' })}>Hủy</Button>
                                    <Button type="primary" htmlType="submit" disabled={actionLoading} icon={<Send size={16} />}>
                                        {actionLoading ? 'Đang gửi...' : 'Gửi đề xuất'}
                                    </Button>
                                </Flex>
                            </Form>
                        )}
                    </>
                )}
            </Modal>
        </div>
    );
};
