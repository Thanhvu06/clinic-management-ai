import { toLocalDateString } from '../../utils/formatters';
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Button, Card, DatePicker, Flex, Form, Input, Modal, Select, Typography } from 'antd';
import { Search, Eye, CheckCircle, XCircle, Clock, PlusCircle, RefreshCw, Send, Pill, Trash2, Plus, Stethoscope } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, DataTable, StatusBadge, LoadingState, Pagination, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './DoctorAppointments.module.css';

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

interface ActiveMedicine {
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
}

interface PrescriptionItemForm {
    medicineId: number;
    quantity: number;
    dosage: string;
    frequency: string;
    durationDays?: number;
    instructions?: string;
}

const statusLabels: Record<string, string> = {
    Pending: 'Chờ xác nhận',
    Confirmed: 'Đã xác nhận (Chờ khám)',
    Completed: 'Đã khám xong',
    Cancelled: 'Đã hủy',
    NoShow: 'Vắng mặt'
};

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
    const [modal, setModal] = useState<{ isOpen: boolean, apt: DoctorAppointment | null, view: 'detail' | 'complete' | 'revisit' | 'noshow' }>({ isOpen: false, apt: null, view: 'detail' });
    const [actionLoading, setActionLoading] = useState(false);

    const [history, setHistory] = useState<any[]>([]);

    // Complete Form
    const [completeSummary, setCompleteSummary] = useState('');
    const [completeInstructions, setCompleteInstructions] = useState('');

    // Prescription Form
    const [activeMedicines, setActiveMedicines] = useState<ActiveMedicine[]>([]);
    const [prescriptionItems, setPrescriptionItems] = useState<PrescriptionItemForm[]>([]);
    const [prescriptionNotes, setPrescriptionNotes] = useState('');
    const [currentPrescription, setCurrentPrescription] = useState<any>(null);

    // No Show Form
    const [noShowReason, setNoShowReason] = useState('');

    // Revisit Form
    const [revisitForm] = Form.useForm();

    useEffect(() => {
        axiosClient.get<any, ApiResponse<ActiveMedicine[]>>('/medicines/active')
            .then(res => {
                if (res.success && res.data) setActiveMedicines(res.data);
            })
            .catch(() => {});
    }, []);

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

    const openModal = async (apt: DoctorAppointment, view: 'detail' | 'complete' | 'revisit' | 'noshow') => {
        setModal({ isOpen: true, apt, view });
        setCompleteSummary('');
        setCompleteInstructions('');
        setNoShowReason('');
        revisitForm.resetFields();
        setPrescriptionItems([]);
        setPrescriptionNotes('');
        setCurrentPrescription(null);

        try {
            const presRes = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments/${apt.id}/prescription`);
            if (presRes.success && presRes.data) {
                setCurrentPrescription(presRes.data);
                if (view === 'complete' && presRes.data.items) {
                    setPrescriptionNotes(presRes.data.notes || '');
                    setPrescriptionItems(presRes.data.items.map((i: any) => ({
                        medicineId: i.medicineId,
                        quantity: i.quantity,
                        dosage: i.dosage,
                        frequency: i.frequency,
                        durationDays: i.durationDays,
                        instructions: i.instructions || ''
                    })));
                }
            }
        } catch {
            setCurrentPrescription(null);
        }

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

    const handleAddMedicineRow = () => {
        if (activeMedicines.length === 0) return;
        setPrescriptionItems(prev => [
            ...prev,
            {
                medicineId: activeMedicines[0].id,
                quantity: 10,
                dosage: '1 viên',
                frequency: '2 lần / ngày sau ăn',
                durationDays: 5,
                instructions: 'Uống sau bữa ăn'
            }
        ]);
    };

    const handleRemoveMedicineRow = (index: number) => {
        setPrescriptionItems(prev => prev.filter((_, i) => i !== index));
    };

    const handleUpdateMedicineRow = (index: number, field: keyof PrescriptionItemForm, value: any) => {
        setPrescriptionItems(prev => {
            const updated = [...prev];
            updated[index] = { ...updated[index], [field]: value };
            return updated;
        });
    };

    const handleComplete = async () => {
        if (!modal.apt) return;

        showConfirm('Xác nhận hoàn thành buổi khám và lưu đơn thuốc?', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/complete`, {
                    summary: completeSummary,
                    followUpInstruction: completeInstructions
                });
                if (res.success) {
                    if (prescriptionItems.length > 0) {
                        await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/prescription`, {
                            notes: prescriptionNotes,
                            items: prescriptionItems
                        });
                    }

                    showAlert('Đã hoàn thành buổi khám và lưu kết quả khám.', 'Thành công', 'success');
                    setModal({ ...modal, view: 'detail' });
                    fetchAppointments();
                }
            } catch (error: any) {
                if (error?.errorCode === 'INVALID_APPOINTMENT_STATUS') showAlert('Trạng thái lịch hẹn không còn hợp lệ (đã được xử lý trước đó).', 'Lỗi', 'error');
                else showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
                fetchAppointments();
            } finally {
                setActionLoading(false);
            }
        });
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

    const closeModal = () => setModal({ isOpen: false, apt: null, view: 'detail' });

    const columns: DataTableColumn<DoctorAppointment>[] = [
        {
            header: 'Giờ khám',
            accessor: (apt) => (
                <div>
                    <div className={styles.timeCell}><Clock size={14} style={{ marginRight: 4, verticalAlign: -2 }} />{apt.startTime.substring(0, 5)}</div>
                    <div className={styles.dateCell}>{formatDate(apt.appointmentDate)}</div>
                    <div className={styles.codeCell}>#{apt.appointmentCode}</div>
                </div>
            )
        },
        {
            header: 'Bệnh nhân',
            accessor: (apt) => (
                <div>
                    <div style={{ fontWeight: 500 }}>{apt.patientName}</div>
                    <div className={styles.patientMeta}>{apt.patientGender} | {apt.patientDob ? formatDate(apt.patientDob) : 'N/A'}</div>
                </div>
            )
        },
        {
            header: 'Lý do khám',
            accessor: (apt) => <div className={styles.reasonCell}>{apt.reason || <Typography.Text type="secondary">Không có ghi chú</Typography.Text>}</div>
        },
        { header: 'Trạng thái', accessor: (apt) => <StatusBadge status={apt.status} label={statusLabels[apt.status] || apt.status} /> },
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
                        <Button type="primary" size="small" icon={<CheckCircle size={14} />} onClick={() => openModal(apt, 'complete')}>
                            Khám
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
                title="Lịch khám của tôi"
                actions={<Button icon={<RefreshCw size={14} />} onClick={() => fetchAppointments()}>Làm mới</Button>}
            />

            <Card size="small" className={styles.filterCard}>
                <Flex gap="middle" wrap>
                    <Input
                        style={{ flex: '1 1 250px' }}
                        prefix={<Search size={16} style={{ color: 'var(--cc-color-text-muted)' }} />}
                        placeholder="Tìm theo mã lịch..."
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                        onPressEnter={handleSearchSubmit}
                    />
                    <Select
                        style={{ width: 200 }}
                        value={dateFilter || undefined}
                        placeholder="Tất cả ngày"
                        allowClear
                        onChange={(val) => { setDateFilter(val || ''); setPage(1); }}
                        options={[{ value: 'today', label: 'Hôm nay' }]}
                    />
                    <Select
                        style={{ width: 220 }}
                        value={statusFilter || undefined}
                        placeholder="Tất cả trạng thái"
                        allowClear
                        onChange={(val) => { setStatusFilter(val || ''); setPage(1); }}
                        options={[
                            { value: 'Confirmed', label: 'Đã xác nhận (Chờ khám)' },
                            { value: 'Completed', label: 'Đã khám xong' },
                            { value: 'NoShow', label: 'Vắng mặt' }
                        ]}
                    />
                    <Button onClick={handleSearchSubmit}>Tìm kiếm</Button>
                </Flex>
            </Card>

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
                    modal.view === 'complete' ? 'Tóm tắt kết quả khám' :
                    modal.view === 'noshow' ? 'Xác nhận vắng mặt' :
                    'Đề xuất tái khám'
                }
                open={modal.isOpen && !!modal.apt}
                onCancel={closeModal}
                footer={null}
                width={640}
                destroyOnHidden
            >
                {modal.apt && (
                    <>
                        <Flex gap="middle" align="center" className={styles.patientInfoBar}>
                            <div className={styles.patientAvatar}>{modal.apt.patientName.charAt(0)}</div>
                            <div>
                                <div style={{ fontWeight: 600, fontSize: '1.1rem' }}>{modal.apt.patientName}</div>
                                <div className={styles.patientMeta}>
                                    {modal.apt.patientGender} | Sinh: {modal.apt.patientDob ? formatDate(modal.apt.patientDob) : 'N/A'} | Lịch hẹn: {formatDate(modal.apt.appointmentDate)} ({modal.apt.startTime.substring(0, 5)})
                                </div>
                            </div>
                        </Flex>

                        {modal.view === 'detail' && (
                            <>
                                <div style={{ marginBottom: 20 }}>
                                    <div className={styles.infoBlockLabel}>Triệu chứng / Lý do khám:</div>
                                    <div className={styles.infoBlockValue}>
                                        {modal.apt.reason || <Typography.Text type="secondary">Không có</Typography.Text>}
                                    </div>
                                </div>
                                <div style={{ marginBottom: 20 }}>
                                    <Flex justify="space-between" style={{ marginBottom: 8 }}>
                                        <span style={{ fontWeight: 500 }}>Trạng thái hiện tại</span>
                                        <StatusBadge status={modal.apt.status} label={statusLabels[modal.apt.status] || modal.apt.status} />
                                    </Flex>
                                    {history.length > 0 && (
                                        <div className={styles.historyTimeline}>
                                            {history.map(h => (
                                                <div key={h.id} className={styles.historyItem}>
                                                    <div className={styles.historyDot}></div>
                                                    <div style={{ fontWeight: 500, fontSize: '0.9rem' }}>{h.action}</div>
                                                    {h.note && <div style={{ fontSize: '0.85rem', color: 'var(--cc-color-text)', marginTop: 4 }}>{h.note}</div>}
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>
                                {modal.apt.status === 'Confirmed' && (
                                    <Flex justify="flex-end" gap="small" style={{ marginTop: 24 }}>
                                        <Button danger icon={<XCircle size={16} />} onClick={() => setModal({ ...modal, view: 'noshow' })}>
                                            Đánh dấu vắng mặt
                                        </Button>
                                        <Button type="primary" icon={<CheckCircle size={16} />} onClick={() => setModal({ ...modal, view: 'complete' })}>
                                            Bắt đầu khám & Kê đơn
                                        </Button>
                                    </Flex>
                                )}

                                {currentPrescription && (
                                    <div style={{ marginTop: 20, borderTop: '1px solid var(--cc-color-border)', paddingTop: 16 }}>
                                        <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
                                            <Flex align="center" gap={8}>
                                                <Pill size={18} style={{ color: 'var(--cc-color-primary)' }} />
                                                <h4 style={{ margin: 0, color: 'var(--cc-color-text-dark)' }}>Đơn thuốc đã kê</h4>
                                            </Flex>
                                            <StatusBadge
                                                status={currentPrescription.status}
                                                label={currentPrescription.status === 'Dispensed' ? 'Đã cấp thuốc' : 'Chờ cấp thuốc (Issued)'}
                                            />
                                        </Flex>
                                        {currentPrescription.notes && (
                                            <div style={{ fontSize: '0.9rem', color: 'var(--cc-color-text)', marginBottom: 10, fontStyle: 'italic' }}>
                                                Ghi chú: {currentPrescription.notes}
                                            </div>
                                        )}
                                        <DataTable
                                            columns={[
                                                { header: 'Tên thuốc', accessor: (item: any) => <span style={{ fontWeight: 600 }}>{item.medicineName} ({item.medicineCode})</span> },
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

                        {modal.view === 'complete' && (
                            <Flex vertical gap="middle">
                                <div className={`${styles.infoBanner} ${styles.infoBannerInfo}`}>
                                    Nhập kết quả khám và kê đơn thuốc cho bệnh nhân. Đơn thuốc sẽ tự động chuyển đến bộ phận Dược sĩ sau khi hoàn thành.
                                </div>
                                <div>
                                    <div className={styles.infoBlockLabel}>Tóm tắt kết quả khám (*)</div>
                                    <Input.TextArea
                                        rows={4}
                                        value={completeSummary}
                                        onChange={e => setCompleteSummary(e.target.value)}
                                        placeholder="Ghi nhận triệu chứng, chẩn đoán sơ bộ..."
                                    />
                                </div>
                                <div>
                                    <div className={styles.infoBlockLabel}>Hướng dẫn theo dõi (Tùy chọn)</div>
                                    <Input.TextArea
                                        rows={2}
                                        value={completeInstructions}
                                        onChange={e => setCompleteInstructions(e.target.value)}
                                        placeholder="Nhắc nhở dùng thuốc, kiêng cữ..."
                                    />
                                </div>

                                <div style={{ borderTop: '1px dashed var(--cc-color-border)', paddingTop: 16 }}>
                                    <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
                                        <Flex align="center" gap={8} style={{ fontWeight: 600, color: 'var(--cc-color-text-dark)' }}>
                                            <Pill size={18} style={{ color: 'var(--cc-color-primary)' }} />
                                            <span>Kê đơn thuốc (Tùy chọn)</span>
                                        </Flex>
                                        <Button size="small" icon={<Plus size={14} />} onClick={handleAddMedicineRow}>
                                            Thêm thuốc
                                        </Button>
                                    </Flex>

                                    {prescriptionItems.length > 0 && (
                                        <Flex vertical gap="small">
                                            {prescriptionItems.map((item, idx) => (
                                                <div key={idx} className={styles.medicineRow}>
                                                    <Flex gap="small" align="center" wrap style={{ marginBottom: 8 }}>
                                                        <Select
                                                            style={{ flex: '2 1 200px' }}
                                                            value={item.medicineId}
                                                            onChange={(val) => handleUpdateMedicineRow(idx, 'medicineId', val)}
                                                            options={activeMedicines.map(m => ({
                                                                value: m.id,
                                                                label: `${m.name} (${m.code}) - Tồn: ${m.stockQuantity} ${m.unit}`
                                                            }))}
                                                        />
                                                        <Input
                                                            type="number"
                                                            min={1}
                                                            style={{ width: 90 }}
                                                            placeholder="SL"
                                                            value={item.quantity}
                                                            onChange={e => handleUpdateMedicineRow(idx, 'quantity', parseInt(e.target.value, 10) || 1)}
                                                        />
                                                        <Input
                                                            style={{ flex: '1 1 120px' }}
                                                            placeholder="Liều dùng (vd: 1 viên)"
                                                            value={item.dosage}
                                                            onChange={e => handleUpdateMedicineRow(idx, 'dosage', e.target.value)}
                                                        />
                                                        <Button
                                                            type="text"
                                                            danger
                                                            icon={<Trash2 size={16} />}
                                                            onClick={() => handleRemoveMedicineRow(idx)}
                                                            title="Xóa thuốc"
                                                        />
                                                    </Flex>
                                                    <Flex gap="small" wrap>
                                                        <Input
                                                            style={{ flex: '1 1 180px' }}
                                                            placeholder="Tần suất (vd: 2 lần/ngày sau ăn)"
                                                            value={item.frequency}
                                                            onChange={e => handleUpdateMedicineRow(idx, 'frequency', e.target.value)}
                                                        />
                                                        <Input
                                                            style={{ flex: '2 1 200px' }}
                                                            placeholder="Hướng dẫn thêm..."
                                                            value={item.instructions || ''}
                                                            onChange={e => handleUpdateMedicineRow(idx, 'instructions', e.target.value)}
                                                        />
                                                    </Flex>
                                                </div>
                                            ))}

                                            <Input
                                                placeholder="Ghi chú đơn thuốc (vd: Uống nhiều nước, kiêng rượu bia...)"
                                                value={prescriptionNotes}
                                                onChange={e => setPrescriptionNotes(e.target.value)}
                                            />
                                        </Flex>
                                    )}
                                </div>

                                <Flex justify="flex-end" gap="small">
                                    <Button onClick={() => setModal({ ...modal, view: 'detail' })}>Quay lại</Button>
                                    <Button type="primary" loading={actionLoading} disabled={!completeSummary} onClick={handleComplete}>
                                        Hoàn thành khám & Lưu đơn thuốc
                                    </Button>
                                </Flex>
                            </Flex>
                        )}

                        {modal.view === 'noshow' && (
                            <Flex vertical gap="middle">
                                <div className={`${styles.infoBanner} ${styles.infoBannerWarning}`}>
                                    Hành động này sẽ hủy buổi khám vì bệnh nhân không đến. Vui lòng xác nhận chắc chắn vì không thể hoàn tác.
                                </div>
                                <div>
                                    <div className={styles.infoBlockLabel}>Ghi chú / Lý do (Tùy chọn)</div>
                                    <Input.TextArea
                                        rows={2}
                                        value={noShowReason}
                                        onChange={e => setNoShowReason(e.target.value)}
                                        placeholder="Ví dụ: Đã gọi điện 3 lần không bắt máy..."
                                    />
                                </div>
                                <Flex justify="flex-end" gap="small">
                                    <Button onClick={() => setModal({ ...modal, view: 'detail' })}>Hủy</Button>
                                    <Button danger type="primary" loading={actionLoading} onClick={handleNoShow}>
                                        Xác nhận vắng mặt
                                    </Button>
                                </Flex>
                            </Flex>
                        )}

                        {modal.view === 'revisit' && (
                            <Form form={revisitForm} layout="vertical" onFinish={handleRevisit}>
                                <Form.Item name="revisitDate" label="Ngày hẹn tái khám đề nghị" rules={[{ required: true, message: 'Vui lòng chọn ngày tái khám' }]}>
                                    <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
                                </Form.Item>
                                <Form.Item name="revisitNote" label="Tin nhắn / Lý do tái khám (Tùy chọn)">
                                    <Input.TextArea rows={3} placeholder="Ví dụ: Tái khám sau khi uống hết thuốc..." />
                                </Form.Item>
                                <Flex justify="flex-end" gap="small">
                                    <Button onClick={() => setModal({ ...modal, view: 'detail' })}>Hủy</Button>
                                    <Button type="primary" htmlType="submit" loading={actionLoading} icon={<Send size={16} />}>
                                        Gửi đề xuất
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
