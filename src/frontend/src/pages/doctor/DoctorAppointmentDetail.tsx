import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { Button, Card, Flex } from 'antd';
import {
    ArrowLeft, Stethoscope, HeartPulse, Pill, History,
    Printer, AlertCircle
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { PatientClinicalContextDto } from '../../types';
import { LoadingState, StatusBadge, DataTable, InlineError, EmptyState } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './DoctorAppointmentDetail.module.css';
import { getAppointmentStatusLabel } from '../../utils/appointmentStatusLabels';
import { formatDateOnly } from '../../utils/formatters';

interface AppointmentHistory {
    id: number;
    action: string;
    oldStatus?: string | null;
    newStatus?: string | null;
    note?: string | null;
    createdAt: string;
}

const actionLabels: Readonly<Record<string, string>> = {
    Created: 'Tạo mới', StatusChanged: 'Cập nhật trạng thái', Cancelled: 'Hủy lịch',
    Rescheduled: 'Đổi lịch', Confirmed: 'Xác nhận', Completed: 'Hoàn thành',
    NoShow: 'Đánh dấu vắng', RescheduleRequested: 'Yêu cầu đổi lịch', CancelRequested: 'Yêu cầu hủy'
};

const statusClass = (status: string) => {
    if (status === 'Cancelled' || status === 'NoShow') return styles.statusDanger;
    if (status.startsWith('Pending')) return styles.statusWarning;
    if (status === 'Completed') return styles.statusSuccess;
    return styles.statusActive;
};

export const DoctorAppointmentDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const appointmentId = Number(id);
    const navigate = useNavigate();

    const [context, setContext] = useState<PatientClinicalContextDto | null>(null);
    const [histories, setHistories] = useState<AppointmentHistory[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [notFound, setNotFound] = useState(false);
    const requestId = useRef(0);

    const loadData = useCallback(async () => {
        if (!appointmentId) return;
        const currentRequest = ++requestId.current;
        setLoading(true);
        setLoadError(null);
        setNotFound(false);
        try {
            const [ctxRes, histRes] = await Promise.all([
                doctorApi.getPatientClinicalContext(appointmentId),
                doctorApi.getAppointmentHistory(appointmentId)
            ]);

            if (currentRequest !== requestId.current) return;
            if (!ctxRes.success || !histRes.success) {
                setContext(null);
                setNotFound(true);
                return;
            }
            if (!ctxRes.data || !histRes.data) throw new Error('Không thể tải chi tiết hồ sơ bệnh án.');
            setContext(ctxRes.data);
            setHistories(histRes.data);
        } catch (err: unknown) {
            if (currentRequest !== requestId.current) return;
            const error = err as { message?: string; status?: number; response?: { status?: number } };
            if ((error.status ?? error.response?.status) === 404) {
                setContext(null);
                setNotFound(true);
            } else {
                setLoadError(error.message || 'Không thể tải chi tiết hồ sơ bệnh án.');
            }
        } finally {
            if (currentRequest === requestId.current) setLoading(false);
        }
    }, [appointmentId]);

    useEffect(() => {
        void loadData();
        return () => { ++requestId.current; };
    }, [loadData]);

    const handlePrint = () => {
        window.print();
    };

    if (loading) {
        return <LoadingState message="Đang tải chi tiết hồ sơ..." height="240px" />;
    }

    if (loadError) {
        return <InlineError message={loadError} onRetry={loadData} />;
    }

    if (notFound) {
        return (
            <Card className={styles.notFoundCard}>
                <AlertCircle size={40} className={styles.icon1} />
                <h3>Không tìm thấy lịch khám</h3>
                <p className={styles.paragraph1}>Hồ sơ không tồn tại hoặc không thuộc quyền quản lý của bạn.</p>
                <Link to="/doctor/appointments">
                    <Button type="primary" className={styles.button1}>Quay lại danh sách</Button>
                </Link>
            </Card>
        );
    }

    if (!context) return null;

    const { currentAppointment: apt, encounter, vitalSigns, prescription } = context;
    const prescriptionItems = prescription?.items ?? [];

    const prescriptionColumns: DataTableColumn<(typeof prescriptionItems)[number]>[] = [
        { header: '#', width: 40, accessor: (item) => prescriptionItems.indexOf(item) + 1 },
        {
            header: 'Tên thuốc',
            accessor: (item) => (
                <div>
                    <div className={styles.text1}>{item.medicineName}</div>
                    <div className={styles.text2}>{item.medicineCode}</div>
                </div>
            )
        },
        { header: 'Số lượng', accessor: (item) => <span className={styles.text3}>{item.quantity} {item.unit}</span> },
        { header: 'Liều dùng', accessor: 'dosage' },
        { header: 'Tần suất', accessor: 'frequency' },
        { header: 'Số ngày', accessor: (item) => item.durationDays ? `${item.durationDays} ngày` : '--' },
        { header: 'Hướng dẫn', accessor: 'instructions' }
    ];

    return (
        <div className={styles.page}>
            <Flex justify="space-between" align="center" wrap gap="small" className={styles.topBar}>
                <Link to="/doctor/appointments" className={styles.backLink}>
                    <ArrowLeft size={16} />
                    <span>Quay lại danh sách lịch khám</span>
                </Link>
                <Flex gap="small">
                    {apt.status === 'InConsultation' && (
                        <Button type="primary" icon={<Stethoscope size={16} />} onClick={() => navigate(`/doctor/appointments/${apt.id}/examination`)}>
                            Tiếp tục phiên khám
                        </Button>
                    )}
                    <Button icon={<Printer size={16} />} onClick={handlePrint}>
                        In hồ sơ bệnh án
                    </Button>
                </Flex>
            </Flex>

            <Card className={`${styles.spotlightCard} ${statusClass(apt.status)}`}>
                <Flex justify="space-between" align="flex-start" wrap gap="small">
                    <div>
                        <Flex align="center" gap={10}>
                            <h1 className={styles.patientName}>{context.patientName}</h1>
                            <span className={styles.codeTag}>#{apt.appointmentCode}</span>
                            <StatusBadge status={apt.status} label={getAppointmentStatusLabel(apt.status)} />
                        </Flex>
                        <div className={styles.metaRow}>
                            <span>{context.patientGender === 'Male' ? 'Nam' : context.patientGender === 'Female' ? 'Nữ' : 'Khác'} {context.patientDob ? `• ${formatDateOnly(context.patientDob)}` : ''}</span>
                            <span>SĐT: {context.patientPhone}</span>
                            {apt.specialtyName && <span>Chuyên khoa: {apt.specialtyName}</span>}
                            {context.address && <span>Địa chỉ: {context.address}</span>}
                            <span>Ngày khám: {formatDateOnly(apt.appointmentDate)} ({apt.startTime.substring(0, 5)} - {apt.endTime.substring(0, 5)})</span>
                        </div>
                        {apt.reason && <p className={styles.appointmentReason}><strong>Lý do khám:</strong> {apt.reason}</p>}
                    </div>
                </Flex>
            </Card>

            <Card className={styles.sectionCard}>
                <div className={styles.sectionTitleRow}>
                    <Stethoscope size={20} className={styles.icon2} />
                    <h2 className={styles.sectionTitle}>Hồ sơ khám lâm sàng</h2>
                </div>

                {encounter ? (
                    <Flex vertical gap={14} className={styles.text4}>
                        <div>
                            <div className={styles.fieldLabel}>Chẩn đoán bệnh</div>
                            <div className={styles.fieldValue}>
                                {encounter.diagnosis || 'Chưa ghi nhận'}
                                {encounter.diagnosisCode && (
                                    <span className={styles.icdTag}>ICD-10: {encounter.diagnosisCode}</span>
                                )}
                            </div>
                        </div>

                        {encounter.chiefComplaint && (
                            <div>
                                <div className={styles.fieldLabel}>Triệu chứng chính / Lý do khám</div>
                                <div className={styles.plainValue}>{encounter.chiefComplaint}</div>
                            </div>
                        )}

                        {encounter.clinicalFindings && (
                            <div>
                                <div className={styles.fieldLabel}>Bệnh sử & Khám thực thể</div>
                                <div className={[styles.plainValue, styles.spacing1].join(' ')}>{encounter.clinicalFindings}</div>
                            </div>
                        )}

                        {encounter.treatmentPlan && (
                            <div>
                                <div className={styles.fieldLabel}>Hướng điều trị</div>
                                <div className={styles.plainValue}>{encounter.treatmentPlan}</div>
                            </div>
                        )}

                        {encounter.summary && (
                            <div>
                                <div className={styles.fieldLabel}>Tóm tắt kết luận</div>
                                <div className={styles.summaryBox}>{encounter.summary}</div>
                            </div>
                        )}

                        {encounter.followUpInstruction && (
                            <div>
                                <div className={styles.fieldLabel}>Dặn dò & Hẹn tái khám</div>
                                <div className={styles.plainValue}>{encounter.followUpInstruction}</div>
                            </div>
                        )}
                    </Flex>
                ) : (
                    <div className={styles.emptyNote}>
                        Chưa có diễn tiến khám lâm sàng nào được lưu trữ cho lịch hẹn này.
                    </div>
                )}
            </Card>

            {vitalSigns && (
                <Card className={styles.sectionCard}>
                    <div className={styles.sectionTitleRow}>
                        <HeartPulse size={20} className={styles.icon3} />
                        <h2 className={styles.sectionTitle}>Dấu hiệu sinh tồn</h2>
                    </div>

                    <div className={styles.vitalsGrid}>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>NHIỆT ĐỘ</div>
                            <div className={styles.vitalValue}>{vitalSigns.temperature ? `${vitalSigns.temperature} °C` : '--'}</div>
                        </div>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>HUYẾT ÁP</div>
                            <div className={styles.vitalValue}>
                                {vitalSigns.bloodPressureSystolic && vitalSigns.bloodPressureDiastolic
                                    ? `${vitalSigns.bloodPressureSystolic}/${vitalSigns.bloodPressureDiastolic} mmHg`
                                    : '--'}
                            </div>
                        </div>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>NHỊP TIM</div>
                            <div className={styles.vitalValue}>{vitalSigns.heartRate ? `${vitalSigns.heartRate} bpm` : '--'}</div>
                        </div>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>NHỊP THỞ</div>
                            <div className={styles.vitalValue}>{vitalSigns.respiratoryRate ? `${vitalSigns.respiratoryRate} /phút` : '--'}</div>
                        </div>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>CHIỀU CAO / CÂN NẶNG</div>
                            <div className={[styles.vitalValue, styles.text5].join(' ')}>
                                {vitalSigns.height ? `${vitalSigns.height}cm` : '--'} / {vitalSigns.weight ? `${vitalSigns.weight}kg` : '--'}
                            </div>
                        </div>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>CHỈ SỐ BMI</div>
                            <div className={[styles.vitalValue, styles.text6].join(' ')}>{vitalSigns.bmi ?? '--'}</div>
                        </div>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>SPO2</div>
                            <div className={styles.vitalValue}>{vitalSigns.spO2 ? `${vitalSigns.spO2} %` : '--'}</div>
                        </div>
                    </div>
                </Card>
            )}

            {prescription && prescription.items.length > 0 && (
                <Card className={styles.sectionCard}>
                    <Flex justify="space-between" align="center" className={styles.sectionTitleRow}>
                        <Flex align="center" gap={8}>
                            <Pill size={20} className={styles.icon4} />
                            <h2 className={styles.sectionTitle}>Đơn thuốc đã cấp</h2>
                        </Flex>
                        <StatusBadge
                            status={prescription.status}
                            label={prescription.status === 'Issued' ? 'Đã phát hành' : prescription.status === 'Dispensed' ? 'Đã cấp phát' : prescription.status}
                        />
                    </Flex>

                    <DataTable
                        columns={prescriptionColumns}
                        data={prescriptionItems}
                        keyExtractor={(item) => item.medicineId}
                    />

                    {prescription.notes && (
                        <div className={styles.notesText}>
                            <strong>Ghi chú:</strong> {prescription.notes}
                        </div>
                    )}
                </Card>
            )}

            <Card className={styles.sectionCard}>
                <div className={styles.sectionTitleRow}>
                    <History size={20} className={styles.sectionIcon} />
                    <h2 className={styles.sectionTitle}>Lần khám trước ({context.totalPastVisits})</h2>
                </div>
                {context.pastVisits.length === 0 ? <EmptyState title="Chưa có lần khám trước." /> : (
                    <div className={styles.pastVisits}>
                        {context.pastVisits.map(visit => (
                            <article key={visit.appointmentId} className={styles.pastVisit}>
                                <Link to={`/doctor/appointments/${visit.appointmentId}`} className={styles.visitLink}>
                                    {formatDateOnly(visit.date)} · {visit.appointmentCode}
                                </Link>
                                <div className={styles.metaRow}>
                                    <span>Bác sĩ: {visit.doctorName}</span><span>Chuyên khoa: {visit.specialtyName}</span>
                                </div>
                                <p><strong>Chẩn đoán:</strong> {visit.diagnosis || 'Chưa ghi nhận'}</p>
                                <p><strong>Thuốc:</strong> {visit.prescriptionItemNames.length > 0 ? visit.prescriptionItemNames.join(', ') : 'Chưa ghi nhận'}</p>
                            </article>
                        ))}
                    </div>
                )}
            </Card>

            {histories.length > 0 && (
                <Card className={styles.sectionCard}>
                    <Flex align="center" gap={8} className={styles.sectionTitleRow}>
                        <History size={20} className={styles.icon5} />
                        <h2 className={styles.sectionTitle}>
                            Nhật ký điều phối & Thao tác
                        </h2>
                    </Flex>

                    <div className={styles.historyList}>
                        {histories.map(h => (
                            <div key={h.id} className={styles.historyRow}>
                                <div>
                                    <span className={styles.historyAction}>{Object.hasOwn(actionLabels, h.action) ? actionLabels[h.action] : h.action}</span>
                                    {(h.oldStatus || h.newStatus) && <div className={styles.historyTransition}>Từ {h.oldStatus ? getAppointmentStatusLabel(h.oldStatus) : '—'} → {h.newStatus ? getAppointmentStatusLabel(h.newStatus) : '—'}</div>}
                                    {h.note && <span className={styles.historyNote}>— {h.note}</span>}
                                </div>
                                <div className={styles.historyDate}>
                                    {new Date(h.createdAt).toLocaleString('vi-VN')}
                                </div>
                            </div>
                        ))}
                    </div>
                </Card>
            )}
        </div>
    );
};
