import React, { useState, useEffect, useCallback } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { 
    ArrowLeft, Stethoscope, HeartPulse, Pill, History, 
    Printer, RefreshCw, AlertCircle 
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { PatientClinicalContextDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';

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
        return (
            <div style={{ textAlign: 'center', padding: '60px 0', color: '#64748b' }}>
                <RefreshCw size={32} className="animate-spin" style={{ margin: '0 auto 12px auto' }} />
                <p>Đang tải chi tiết hồ sơ...</p>
            </div>
        );
    }

    if (!context) {
        return (
            <div className="card" style={{ padding: '36px', textAlign: 'center', margin: '30px auto', maxWidth: '480px' }}>
                <AlertCircle size={40} style={{ color: '#ef4444', margin: '0 auto 12px auto' }} />
                <h3>Không tìm thấy lịch khám</h3>
                <p style={{ color: '#64748b', fontSize: '0.9rem' }}>Hồ sơ không tồn tại hoặc không thuộc quyền quản lý của bạn.</p>
                <Link to="/doctor/appointments" className="btn-primary" style={{ display: 'inline-block', marginTop: '14px' }}>
                    Quay lại danh sách
                </Link>
            </div>
        );
    }

    const { currentAppointment: apt, encounter, vitalSigns, prescription } = context;

    return (
        <div style={{ padding: '8px 0', maxWidth: '1000px', margin: '0 auto' }}>
            {/* Header Actions */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px', flexWrap: 'wrap', gap: '12px' }}>
                <Link to="/doctor/appointments" style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: '#0284c7', textDecoration: 'none', fontWeight: 600 }}>
                    <ArrowLeft size={16} />
                    <span>Quay lại danh sách lịch khám</span>
                </Link>
                <div style={{ display: 'flex', gap: '10px' }}>
                    {apt.status === 'InConsultation' && (
                        <button
                            className="btn-primary"
                            onClick={() => navigate(`/doctor/appointments/${apt.id}/examination`)}
                            style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 16px', borderRadius: '6px', backgroundColor: '#4f46e5', cursor: 'pointer' }}
                        >
                            <Stethoscope size={16} />
                            <span>Tiếp tục phiên khám</span>
                        </button>
                    )}
                    <button
                        className="btn-secondary"
                        onClick={handlePrint}
                        style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}
                    >
                        <Printer size={16} />
                        <span>In hồ sơ bệnh án</span>
                    </button>
                </div>
            </div>

            {/* Patient Spotlight Header */}
            <div className="card" style={{ padding: '20px 24px', marginBottom: '20px', borderRadius: '10px', borderLeft: '5px solid #059669' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '12px' }}>
                    <div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                            <h1 style={{ margin: 0, fontSize: '1.4rem', fontWeight: 800, color: '#0f172a' }}>
                                {context.patientName}
                            </h1>
                            <span style={{ backgroundColor: '#f1f5f9', color: '#475569', padding: '2px 8px', borderRadius: '4px', fontSize: '0.8rem', fontWeight: 700, fontFamily: 'monospace' }}>
                                #{apt.appointmentCode}
                            </span>
                            <span style={{ 
                                backgroundColor: apt.status === 'Completed' ? '#dcfce7' : '#e0e7ff',
                                color: apt.status === 'Completed' ? '#15803d' : '#4338ca',
                                padding: '2px 10px', borderRadius: '20px', fontSize: '0.8rem', fontWeight: 700 
                            }}>
                                {apt.status === 'Completed' ? 'Đã hoàn tất khám' : apt.status}
                            </span>
                        </div>

                        <div style={{ display: 'flex', gap: '20px', marginTop: '8px', flexWrap: 'wrap', fontSize: '0.9rem', color: '#475569' }}>
                            <span>{context.patientGender === 'Male' ? 'Nam' : context.patientGender === 'Female' ? 'Nữ' : 'Khác'} {context.patientDob ? `• ${context.patientDob}` : ''}</span>
                            <span>SĐT: {context.patientPhone}</span>
                            <span>Ngày khám: {apt.appointmentDate} ({apt.startTime.substring(0, 5)} - {apt.endTime.substring(0, 5)})</span>
                        </div>
                    </div>
                </div>
            </div>

            {/* Clinical Encounter Section */}
            <div className="card" style={{ padding: '24px', marginBottom: '20px', borderRadius: '8px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '16px', borderBottom: '1px solid #e2e8f0', paddingBottom: '10px' }}>
                    <Stethoscope size={20} style={{ color: '#0284c7' }} />
                    <h2 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 700, color: '#0f172a' }}>
                        Hồ sơ khám lâm sàng
                    </h2>
                </div>

                {encounter ? (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '14px', fontSize: '0.92rem' }}>
                        <div>
                            <div style={{ fontWeight: 700, color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>Chẩn đoán bệnh</div>
                            <div style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {encounter.diagnosis || 'Chưa ghi nhận'}
                                {encounter.diagnosisCode && (
                                    <span style={{ marginLeft: '8px', backgroundColor: '#e0f2fe', color: '#0369a1', padding: '2px 6px', borderRadius: '4px', fontSize: '0.8rem', fontWeight: 600 }}>
                                        ICD-10: {encounter.diagnosisCode}
                                    </span>
                                )}
                            </div>
                        </div>

                        {encounter.chiefComplaint && (
                            <div>
                                <div style={{ fontWeight: 700, color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>Triệu chứng chính / Lý do khám</div>
                                <div style={{ color: '#1e293b', marginTop: '2px' }}>{encounter.chiefComplaint}</div>
                            </div>
                        )}

                        {encounter.clinicalFindings && (
                            <div>
                                <div style={{ fontWeight: 700, color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>Bệnh sử & Khám thực thể</div>
                                <div style={{ color: '#1e293b', marginTop: '2px', whiteSpace: 'pre-wrap' }}>{encounter.clinicalFindings}</div>
                            </div>
                        )}

                        {encounter.treatmentPlan && (
                            <div>
                                <div style={{ fontWeight: 700, color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>Hướng điều trị</div>
                                <div style={{ color: '#1e293b', marginTop: '2px' }}>{encounter.treatmentPlan}</div>
                            </div>
                        )}

                        {encounter.summary && (
                            <div>
                                <div style={{ fontWeight: 700, color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>Tóm tắt kết luận</div>
                                <div style={{ color: '#1e293b', marginTop: '2px', backgroundColor: '#f8fafc', padding: '10px', borderRadius: '6px', borderLeft: '3px solid #0284c7' }}>
                                    {encounter.summary}
                                </div>
                            </div>
                        )}

                        {encounter.followUpInstruction && (
                            <div>
                                <div style={{ fontWeight: 700, color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>Dặn dò & Hẹn tái khám</div>
                                <div style={{ color: '#1e293b', marginTop: '2px' }}>{encounter.followUpInstruction}</div>
                            </div>
                        )}
                    </div>
                ) : (
                    <div style={{ color: '#64748b', fontStyle: 'italic', padding: '12px 0' }}>
                        Chưa có diễn tiến khám lâm sàng nào được lưu trữ cho lịch hẹn này.
                    </div>
                )}
            </div>

            {/* Vital Signs Section */}
            {vitalSigns && (
                <div className="card" style={{ padding: '24px', marginBottom: '20px', borderRadius: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '16px', borderBottom: '1px solid #e2e8f0', paddingBottom: '10px' }}>
                        <HeartPulse size={20} style={{ color: '#e11d48' }} />
                        <h2 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 700, color: '#0f172a' }}>
                            Dấu hiệu sinh tồn
                        </h2>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: '14px' }}>
                        <div style={{ backgroundColor: '#f8fafc', padding: '12px', borderRadius: '6px' }}>
                            <div style={{ fontSize: '0.75rem', color: '#64748b', fontWeight: 600 }}>NHIỆT ĐỘ</div>
                            <div style={{ fontSize: '1.2rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {vitalSigns.temperature ? `${vitalSigns.temperature} °C` : '--'}
                            </div>
                        </div>

                        <div style={{ backgroundColor: '#f8fafc', padding: '12px', borderRadius: '6px' }}>
                            <div style={{ fontSize: '0.75rem', color: '#64748b', fontWeight: 600 }}>HUYẾT ÁP</div>
                            <div style={{ fontSize: '1.2rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {vitalSigns.bloodPressureSystolic && vitalSigns.bloodPressureDiastolic 
                                    ? `${vitalSigns.bloodPressureSystolic}/${vitalSigns.bloodPressureDiastolic} mmHg` 
                                    : '--'}
                            </div>
                        </div>

                        <div style={{ backgroundColor: '#f8fafc', padding: '12px', borderRadius: '6px' }}>
                            <div style={{ fontSize: '0.75rem', color: '#64748b', fontWeight: 600 }}>NHỊP TIM</div>
                            <div style={{ fontSize: '1.2rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {vitalSigns.heartRate ? `${vitalSigns.heartRate} bpm` : '--'}
                            </div>
                        </div>

                        <div style={{ backgroundColor: '#f8fafc', padding: '12px', borderRadius: '6px' }}>
                            <div style={{ fontSize: '0.75rem', color: '#64748b', fontWeight: 600 }}>NHỊP THỞ</div>
                            <div style={{ fontSize: '1.2rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {vitalSigns.respiratoryRate ? `${vitalSigns.respiratoryRate} /phút` : '--'}
                            </div>
                        </div>

                        <div style={{ backgroundColor: '#f8fafc', padding: '12px', borderRadius: '6px' }}>
                            <div style={{ fontSize: '0.75rem', color: '#64748b', fontWeight: 600 }}>CHIỀU CAO / CÂN NẶNG</div>
                            <div style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {vitalSigns.height ? `${vitalSigns.height}cm` : '--'} / {vitalSigns.weight ? `${vitalSigns.weight}kg` : '--'}
                            </div>
                        </div>

                        <div style={{ backgroundColor: '#f8fafc', padding: '12px', borderRadius: '6px' }}>
                            <div style={{ fontSize: '0.75rem', color: '#64748b', fontWeight: 600 }}>CHỈ SỐ BMI</div>
                            <div style={{ fontSize: '1.2rem', fontWeight: 800, color: '#0f172a', marginTop: '2px' }}>
                                {vitalSigns.bmi ?? '--'}
                            </div>
                        </div>

                        <div style={{ backgroundColor: '#f8fafc', padding: '12px', borderRadius: '6px' }}>
                            <div style={{ fontSize: '0.75rem', color: '#64748b', fontWeight: 600 }}>SPO2</div>
                            <div style={{ fontSize: '1.2rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {vitalSigns.spO2 ? `${vitalSigns.spO2} %` : '--'}
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Prescription Section */}
            {prescription && prescription.items.length > 0 && (
                <div className="card" style={{ padding: '24px', marginBottom: '20px', borderRadius: '8px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', borderBottom: '1px solid #e2e8f0', paddingBottom: '10px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <Pill size={20} style={{ color: '#059669' }} />
                            <h2 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 700, color: '#0f172a' }}>
                                Đơn thuốc đã cấp
                            </h2>
                        </div>
                        <span style={{ 
                            backgroundColor: prescription.status === 'Issued' ? '#dcfce7' : prescription.status === 'Dispensed' ? '#e0e7ff' : '#f1f5f9',
                            color: prescription.status === 'Issued' ? '#15803d' : prescription.status === 'Dispensed' ? '#4338ca' : '#475569',
                            padding: '3px 10px', borderRadius: '20px', fontSize: '0.8rem', fontWeight: 600 
                        }}>
                            {prescription.status === 'Issued' ? 'Đã phát hành' : prescription.status === 'Dispensed' ? 'Đã cấp phát' : prescription.status}
                        </span>
                    </div>

                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
                        <thead>
                            <tr style={{ borderBottom: '2px solid #e2e8f0', textAlign: 'left', color: '#475569', fontSize: '0.8rem' }}>
                                <th style={{ padding: '10px' }}>#</th>
                                <th style={{ padding: '10px' }}>Tên thuốc</th>
                                <th style={{ padding: '10px' }}>Số lượng</th>
                                <th style={{ padding: '10px' }}>Liều dùng</th>
                                <th style={{ padding: '10px' }}>Tần suất</th>
                                <th style={{ padding: '10px' }}>Số ngày</th>
                                <th style={{ padding: '10px' }}>Hướng dẫn</th>
                            </tr>
                        </thead>
                        <tbody>
                            {prescription.items.map((item, idx) => (
                                <tr key={item.medicineId} style={{ borderBottom: '1px solid #f1f5f9' }}>
                                    <td style={{ padding: '10px', fontWeight: 600 }}>{idx + 1}</td>
                                    <td style={{ padding: '10px' }}>
                                        <div style={{ fontWeight: 600, color: '#0f172a' }}>{item.medicineName}</div>
                                        <div style={{ fontSize: '0.75rem', color: '#64748b' }}>{item.medicineCode}</div>
                                    </td>
                                    <td style={{ padding: '10px', fontWeight: 700 }}>{item.quantity} {item.unit}</td>
                                    <td style={{ padding: '10px' }}>{item.dosage}</td>
                                    <td style={{ padding: '10px' }}>{item.frequency}</td>
                                    <td style={{ padding: '10px' }}>{item.durationDays ? `${item.durationDays} ngày` : '--'}</td>
                                    <td style={{ padding: '10px' }}>{item.instructions}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>

                    {prescription.notes && (
                        <div style={{ marginTop: '14px', fontSize: '0.85rem', color: '#475569' }}>
                            <strong>Ghi chú:</strong> {prescription.notes}
                        </div>
                    )}
                </div>
            )}

            {/* History Log */}
            {histories.length > 0 && (
                <div className="card" style={{ padding: '20px 24px', borderRadius: '8px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '14px' }}>
                        <History size={18} style={{ color: '#64748b' }} />
                        <h3 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700, color: '#0f172a' }}>
                            Nhật ký điều phối & Thao tác
                        </h3>
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', fontSize: '0.85rem' }}>
                        {histories.map(h => (
                            <div key={h.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 12px', backgroundColor: '#f8fafc', borderRadius: '6px' }}>
                                <div>
                                    <span style={{ fontWeight: 600, color: '#0f172a' }}>{h.action}</span>
                                    {h.note && <span style={{ color: '#475569', marginLeft: '8px' }}>— {h.note}</span>}
                                </div>
                                <div style={{ color: '#94a3b8', fontSize: '0.8rem' }}>
                                    {new Date(h.createdAt).toLocaleString('vi-VN')}
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
};
