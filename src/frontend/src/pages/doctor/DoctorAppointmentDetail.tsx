import React, { useState, useEffect, useCallback } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { Button, Card, Flex } from 'antd';
import {
    ArrowLeft, Stethoscope, HeartPulse, Pill, History,
    Printer, AlertCircle
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { PatientClinicalContextDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { LoadingState, StatusBadge, DataTable } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './DoctorAppointmentDetail.module.css';

export const DoctorAppointmentDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const appointmentId = Number(id);
    const navigate = useNavigate();
    const { showAlert } = useDialog();

    const [context, setContext] = useState<PatientClinicalContextDto | null>(null);
    const [histories, setHistories] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);

    const loadData = useCallback(async () => {
        if (!appointmentId) return;
        setLoading(true);
        try {
            const [ctxRes, histRes] = await Promise.all([
                doctorApi.getPatientClinicalContext(appointmentId),
                doctorApi.getAppointmentHistory(appointmentId)
            ]);

            if (ctxRes.success && ctxRes.data) {
                setContext(ctxRes.data);
            }
            if (histRes.success && histRes.data) {
                setHistories(histRes.data);
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tải chi tiết hồ sơ bệnh án.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    }, [appointmentId, showAlert]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handlePrint = () => {
        window.print();
    };

    if (loading) {
        return <LoadingState message="Đang tải chi tiết hồ sơ..." height="240px" />;
    }

    if (!context) {
        return (
            <Card className={styles.notFoundCard}>
                <AlertCircle size={40} style={{ color: 'var(--cc-color-danger)', margin: '0 auto 12px auto' }} />
                <h3>Không tìm thấy lịch khám</h3>
                <p style={{ color: 'var(--cc-color-text-muted)', fontSize: '0.9rem' }}>Hồ sơ không tồn tại hoặc không thuộc quyền quản lý của bạn.</p>
                <Link to="/doctor/appointments">
                    <Button type="primary" style={{ marginTop: 14 }}>Quay lại danh sách</Button>
                </Link>
            </Card>
        );
    }

    const { currentAppointment: apt, encounter, vitalSigns, prescription } = context;
    const prescriptionItems = prescription?.items ?? [];

    const prescriptionColumns: DataTableColumn<(typeof prescriptionItems)[number]>[] = [
        { header: '#', width: 40, accessor: (item) => prescriptionItems.indexOf(item) + 1 },
        {
            header: 'Tên thuốc',
            accessor: (item) => (
                <div>
                    <div style={{ fontWeight: 600, color: 'var(--cc-color-text-dark)' }}>{item.medicineName}</div>
                    <div style={{ fontSize: '0.75rem', color: 'var(--cc-color-text-muted)' }}>{item.medicineCode}</div>
                </div>
            )
        },
        { header: 'Số lượng', accessor: (item) => <span style={{ fontWeight: 700 }}>{item.quantity} {item.unit}</span> },
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

            <Card className={styles.spotlightCard}>
                <Flex justify="space-between" align="flex-start" wrap gap="small">
                    <div>
                        <Flex align="center" gap={10}>
                            <h1 className={styles.patientName}>{context.patientName}</h1>
                            <span className={styles.codeTag}>#{apt.appointmentCode}</span>
                            <StatusBadge status={apt.status} label={apt.status === 'Completed' ? 'Đã hoàn tất khám' : apt.status} />
                        </Flex>
                        <div className={styles.metaRow}>
                            <span>{context.patientGender === 'Male' ? 'Nam' : context.patientGender === 'Female' ? 'Nữ' : 'Khác'} {context.patientDob ? `• ${context.patientDob}` : ''}</span>
                            <span>SĐT: {context.patientPhone}</span>
                            <span>Ngày khám: {apt.appointmentDate} ({apt.startTime.substring(0, 5)} - {apt.endTime.substring(0, 5)})</span>
                        </div>
                    </div>
                </Flex>
            </Card>

            <Card className={styles.sectionCard}>
                <div className={styles.sectionTitleRow}>
                    <Stethoscope size={20} style={{ color: 'var(--cc-color-primary)' }} />
                    <h2 className={styles.sectionTitle}>Hồ sơ khám lâm sàng</h2>
                </div>

                {encounter ? (
                    <Flex vertical gap={14} style={{ fontSize: '0.92rem' }}>
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
                                <div className={styles.plainValue} style={{ whiteSpace: 'pre-wrap' }}>{encounter.clinicalFindings}</div>
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
                        <HeartPulse size={20} style={{ color: 'var(--cc-color-danger)' }} />
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
                            <div className={styles.vitalValue} style={{ fontSize: '1.1rem' }}>
                                {vitalSigns.height ? `${vitalSigns.height}cm` : '--'} / {vitalSigns.weight ? `${vitalSigns.weight}kg` : '--'}
                            </div>
                        </div>
                        <div className={styles.vitalBox}>
                            <div className={styles.vitalLabel}>CHỈ SỐ BMI</div>
                            <div className={styles.vitalValue} style={{ fontWeight: 800 }}>{vitalSigns.bmi ?? '--'}</div>
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
                            <Pill size={20} style={{ color: 'var(--cc-color-success)' }} />
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

            {histories.length > 0 && (
                <Card>
                    <Flex align="center" gap={8} style={{ marginBottom: 14 }}>
                        <History size={18} style={{ color: 'var(--cc-color-text-muted)' }} />
                        <h3 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700, color: 'var(--cc-color-text-dark)' }}>
                            Nhật ký điều phối & Thao tác
                        </h3>
                    </Flex>

                    <div className={styles.historyList}>
                        {histories.map(h => (
                            <div key={h.id} className={styles.historyRow}>
                                <div>
                                    <span className={styles.historyAction}>{h.action}</span>
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
