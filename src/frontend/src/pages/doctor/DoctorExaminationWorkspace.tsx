import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { 
    Stethoscope, HeartPulse, Pill, History, Save, CheckCircle, 
    AlertCircle, ArrowLeft, Trash2, Search, Clock, Calendar, 
    User, Phone, MapPin, RefreshCw 
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { 
    PatientClinicalContextDto,
    SaveEncounterRequest,
    SaveVitalSignsRequest,
    SavePrescriptionDraftRequest,
    CompleteConsultationRequest
} from '../../types';
import { useDialog } from '../../contexts/DialogContext';

interface ActiveMedicine {
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
}

export const DoctorExaminationWorkspace: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const appointmentId = Number(id);
    const navigate = useNavigate();
    const { showAlert, showToast } = useDialog();

    const [context, setContext] = useState<PatientClinicalContextDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [activeTab, setActiveTab] = useState<'encounter' | 'vitals' | 'prescription' | 'history'>('encounter');

    // Medicine Catalog
    const [medicines, setMedicines] = useState<ActiveMedicine[]>([]);
    const [medicineSearch, setMedicineSearch] = useState('');

    // Tab 1: Encounter Form
    const [chiefComplaint, setChiefComplaint] = useState('');
    const [clinicalFindings, setClinicalFindings] = useState('');
    const [diagnosis, setDiagnosis] = useState('');
    const [diagnosisCode, setDiagnosisCode] = useState('');
    const [treatmentPlan, setTreatmentPlan] = useState('');
    const [summary, setSummary] = useState('');
    const [followUpInstruction, setFollowUpInstruction] = useState('');
    const [encounterRowVersion, setEncounterRowVersion] = useState<string | null>(null);
    const [savingEncounter, setSavingEncounter] = useState(false);

    // Tab 2: Vitals Form
    const [temperature, setTemperature] = useState<string>('');
    const [bpSystolic, setBpSystolic] = useState<string>('');
    const [bpDiastolic, setBpDiastolic] = useState<string>('');
    const [heartRate, setHeartRate] = useState<string>('');
    const [respiratoryRate, setRespiratoryRate] = useState<string>('');
    const [weight, setWeight] = useState<string>('');
    const [height, setHeight] = useState<string>('');
    const [spO2, setSpO2] = useState<string>('');
    const [vitalsRowVersion, setVitalsRowVersion] = useState<string | null>(null);
    const [savingVitals, setSavingVitals] = useState(false);

    // Tab 3: Prescription Form
    const [prescriptionNotes, setPrescriptionNotes] = useState('');
    const [prescriptionItems, setPrescriptionItems] = useState<Array<{
        medicineId: number;
        medicineCode: string;
        medicineName: string;
        unit: string;
        availableStock: number;
        quantity: number;
        dosage: string;
        frequency: string;
        durationDays: number;
        instructions: string;
    }>>([]);
    const [prescriptionRowVersion, setPrescriptionRowVersion] = useState<string | null>(null);
    const [savingPrescription, setSavingPrescription] = useState(false);

    // Revisit Modal
    const [isRevisitModalOpen, setIsRevisitModalOpen] = useState(false);
    const [revisitDate, setRevisitDate] = useState('');
    const [revisitNote, setRevisitNote] = useState('');
    const [savingRevisit, setSavingRevisit] = useState(false);

    // Complete Consultation Modal
    const [isCompleteModalOpen, setIsCompleteModalOpen] = useState(false);
    const [issuePrescriptionCheck, setIssuePrescriptionCheck] = useState(true);
    const [completing, setCompleting] = useState(false);

    // Load initial context
    const loadContext = useCallback(async () => {
        if (!appointmentId) return;
        setLoading(true);
        try {
            const [ctxRes, medRes] = await Promise.all([
                doctorApi.getPatientClinicalContext(appointmentId),
                doctorApi.getActiveMedicines()
            ]);

            if (ctxRes.success && ctxRes.data) {
                const data = ctxRes.data;
                setContext(data);

                // Populate Encounter
                if (data.encounter) {
                    setChiefComplaint(data.encounter.chiefComplaint || data.currentAppointment.reason || '');
                    setClinicalFindings(data.encounter.clinicalFindings || '');
                    setDiagnosis(data.encounter.diagnosis || '');
                    setDiagnosisCode(data.encounter.diagnosisCode || '');
                    setTreatmentPlan(data.encounter.treatmentPlan || '');
                    setSummary(data.encounter.summary || '');
                    setFollowUpInstruction(data.encounter.followUpInstruction || '');
                    setEncounterRowVersion(data.encounter.rowVersion || null);
                } else {
                    setChiefComplaint(data.currentAppointment.reason || '');
                }

                // Populate Vitals
                if (data.vitalSigns) {
                    setTemperature(data.vitalSigns.temperature?.toString() || '');
                    setBpSystolic(data.vitalSigns.bloodPressureSystolic?.toString() || '');
                    setBpDiastolic(data.vitalSigns.bloodPressureDiastolic?.toString() || '');
                    setHeartRate(data.vitalSigns.heartRate?.toString() || '');
                    setRespiratoryRate(data.vitalSigns.respiratoryRate?.toString() || '');
                    setWeight(data.vitalSigns.weight?.toString() || '');
                    setHeight(data.vitalSigns.height?.toString() || '');
                    setSpO2(data.vitalSigns.spO2?.toString() || '');
                    setVitalsRowVersion(data.vitalSigns.rowVersion || null);
                }

                // Populate Prescription Draft
                if (data.prescription) {
                    setPrescriptionNotes(data.prescription.notes || '');
                    setPrescriptionRowVersion(data.prescription.rowVersion || null);
                    setPrescriptionItems(data.prescription.items.map(i => ({
                        medicineId: i.medicineId,
                        medicineCode: i.medicineCode,
                        medicineName: i.medicineName,
                        unit: i.unit,
                        availableStock: i.availableStock,
                        quantity: i.quantity,
                        dosage: i.dosage || '1 viên',
                        frequency: i.frequency || 'Ngày 2 lần',
                        durationDays: i.durationDays || 5,
                        instructions: i.instructions || 'Uống sau bữa ăn'
                    })));
                }
            }

            if (medRes.success && medRes.data) {
                setMedicines(medRes.data);
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tải hồ sơ khám lâm sàng của bệnh nhân.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    }, [appointmentId, showAlert]);

    useEffect(() => {
        loadContext();
    }, [loadContext]);

    // Computed BMI
    const computedBmi = useMemo(() => {
        const w = parseFloat(weight);
        const h = parseFloat(height);
        if (!w || !h || h <= 0) return null;
        const hMeter = h / 100;
        const bmi = w / (hMeter * hMeter);
        return Math.round(bmi * 10) / 10;
    }, [weight, height]);

    const bmiClassification = useMemo(() => {
        if (!computedBmi) return null;
        if (computedBmi < 18.5) return { label: 'Thiếu cân (Gầy)', color: '#d97706' };
        if (computedBmi < 25.0) return { label: 'Bình thường (Lý tưởng)', color: '#15803d' };
        if (computedBmi < 30.0) return { label: 'Tiền béo phì', color: '#ea580c' };
        return { label: 'Béo phì', color: '#dc2626' };
    }, [computedBmi]);

    const minRevisitDate = useMemo(() => {
        const tomorrow = new Date();
        tomorrow.setDate(tomorrow.getDate() + 1);
        return tomorrow.toISOString().split('T')[0];
    }, []);

    // Handlers: Save Encounter
    const handleSaveEncounter = async () => {
        setSavingEncounter(true);
        try {
            const req: SaveEncounterRequest = {
                chiefComplaint,
                clinicalFindings,
                diagnosis,
                diagnosisCode,
                treatmentPlan,
                summary,
                followUpInstruction,
                rowVersion: encounterRowVersion
            };
            const res = await doctorApi.saveEncounter(appointmentId, req);
            if (res.success && res.data) {
                setEncounterRowVersion(res.data.rowVersion || null);
                showToast('Đã lưu diễn tiến khám lâm sàng.', 'success');
            }
        } catch (err: any) {
            if (err.response?.status === 409) {
                showAlert('Hồ sơ khám đã được chỉnh sửa bởi phiên khác. Đang tải lại dữ liệu mới nhất...', 'Xung đột dữ liệu', 'warning');
                await loadContext();
            } else {
                showAlert(err.response?.data?.message || 'Không thể lưu diễn tiến khám.', 'Lỗi', 'error');
            }
        } finally {
            setSavingEncounter(false);
        }
    };

    // Handlers: Save Vitals
    const handleSaveVitals = async () => {
        setSavingVitals(true);
        try {
            const req: SaveVitalSignsRequest = {
                temperature: temperature ? parseFloat(temperature) : null,
                bloodPressureSystolic: bpSystolic ? parseInt(bpSystolic, 10) : null,
                bloodPressureDiastolic: bpDiastolic ? parseInt(bpDiastolic, 10) : null,
                heartRate: heartRate ? parseInt(heartRate, 10) : null,
                respiratoryRate: respiratoryRate ? parseInt(respiratoryRate, 10) : null,
                weight: weight ? parseFloat(weight) : null,
                height: height ? parseFloat(height) : null,
                spO2: spO2 ? parseInt(spO2, 10) : null,
                rowVersion: vitalsRowVersion
            };
            const res = await doctorApi.saveVitalSigns(appointmentId, req);
            if (res.success && res.data) {
                setVitalsRowVersion(res.data.rowVersion || null);
                showToast('Đã lưu dấu hiệu sinh tồn thành công.', 'success');
            }
        } catch (err: any) {
            if (err.response?.status === 409) {
                showAlert('Dấu hiệu sinh tồn đã bị thay đổi bởi phiên làm việc khác. Đang tải lại...', 'Xung đột dữ liệu', 'warning');
                await loadContext();
            } else {
                showAlert(err.response?.data?.message || 'Không thể lưu dấu hiệu sinh tồn.', 'Lỗi', 'error');
            }
        } finally {
            setSavingVitals(false);
        }
    };

    // Handlers: Save Prescription Draft
    const handleSavePrescriptionDraft = async () => {
        setSavingPrescription(true);
        try {
            const req: SavePrescriptionDraftRequest = {
                notes: prescriptionNotes,
                rowVersion: prescriptionRowVersion,
                items: prescriptionItems.map(item => ({
                    medicineId: item.medicineId,
                    quantity: item.quantity,
                    dosage: item.dosage,
                    frequency: item.frequency,
                    durationDays: item.durationDays,
                    instructions: item.instructions
                }))
            };
            const res = await doctorApi.savePrescriptionDraft(appointmentId, req);
            if (res.success && res.data) {
                setPrescriptionRowVersion(res.data.rowVersion || null);
                showToast('Đã lưu nháp đơn thuốc thành công.', 'success');
            }
        } catch (err: any) {
            if (err.response?.status === 409) {
                showAlert('Đơn thuốc đã bị thay đổi bởi phiên khác. Đang làm mới...', 'Xung đột dữ liệu', 'warning');
                await loadContext();
            } else {
                showAlert(err.response?.data?.message || 'Không thể lưu đơn thuốc.', 'Lỗi', 'error');
            }
        } finally {
            setSavingPrescription(false);
        }
    };

    // Prescription Medicine Add/Remove
    const handleAddMedicine = (med: ActiveMedicine) => {
        const existing = prescriptionItems.find(i => i.medicineId === med.id);
        if (existing) {
            showAlert(`Thuốc "${med.name}" đã có trong danh sách đơn thuốc.`, 'Đã tồn tại', 'info');
            return;
        }

        setPrescriptionItems(prev => [
            ...prev,
            {
                medicineId: med.id,
                medicineCode: med.code,
                medicineName: med.name,
                unit: med.unit,
                availableStock: med.stockQuantity,
                quantity: 1,
                dosage: '1 viên',
                frequency: 'Ngày 2 lần',
                durationDays: 5,
                instructions: 'Uống sau bữa ăn'
            }
        ]);
        setMedicineSearch('');
    };

    const handleRemoveMedicine = (index: number) => {
        setPrescriptionItems(prev => prev.filter((_, idx) => idx !== index));
    };

    const handleItemChange = (index: number, field: string, value: any) => {
        setPrescriptionItems(prev => {
            const updated = [...prev];
            updated[index] = { ...updated[index], [field]: value };
            return updated;
        });
    };

    // Handle Complete Consultation
    const handleCompleteConsultation = async () => {
        if (!diagnosis.trim()) {
            showAlert('Vui lòng nhập chẩn đoán bệnh trước khi hoàn tất khám.', 'Thiếu thông tin', 'warning');
            setActiveTab('encounter');
            return;
        }

        if (!summary.trim()) {
            showAlert('Vui lòng nhập tóm tắt kết luận khám trước khi hoàn tất.', 'Thiếu thông tin', 'warning');
            setActiveTab('encounter');
            return;
        }

        setCompleting(true);
        try {
            const req: CompleteConsultationRequest = {
                chiefComplaint,
                clinicalFindings,
                diagnosis: diagnosis.trim(),
                diagnosisCode: diagnosisCode.trim() || undefined,
                treatmentPlan,
                summary: summary.trim(),
                followUpInstruction,
                encounterRowVersion,
                issuePrescription: issuePrescriptionCheck && prescriptionItems.length > 0,
                prescriptionNotes,
                prescriptionRowVersion,
                prescriptionItems: issuePrescriptionCheck && prescriptionItems.length > 0 ? prescriptionItems.map(i => ({
                    medicineId: i.medicineId,
                    quantity: i.quantity,
                    dosage: i.dosage,
                    frequency: i.frequency,
                    durationDays: i.durationDays,
                    instructions: i.instructions
                })) : undefined
            };

            const res = await doctorApi.completeConsultation(appointmentId, req);
            if (res.success) {
                showToast('Đã hoàn tất ca khám lâm sàng và cấp hồ sơ bệnh án thành công!', 'success');
                setIsCompleteModalOpen(false);
                navigate(`/doctor/appointments/${appointmentId}`);
            }
        } catch (err: any) {
            if (err.response?.status === 409) {
                showAlert('Hồ sơ bệnh án đã bị sửa đổi đồng thời bởi một phiên khác. Vui lòng tải lại và kiểm tra.', 'Xung đột dữ liệu', 'error');
                await loadContext();
            } else {
                showAlert(err.response?.data?.message || 'Không thể hoàn tất ca khám.', 'Lỗi', 'error');
            }
        } finally {
            setCompleting(false);
        }
    };

    // Handle Revisit Request
    const handleCreateRevisit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!revisitDate) {
            showAlert('Vui lòng chọn ngày hẹn tái khám.', 'Thiếu ngày', 'warning');
            return;
        }

        setSavingRevisit(true);
        try {
            const res = await doctorApi.createRevisitRequest(appointmentId, revisitDate, revisitNote);
            if (res.success) {
                showToast('Đã tạo đề xuất tái khám cho bệnh nhân thành công!', 'success');
                setIsRevisitModalOpen(false);
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tạo đề xuất tái khám.', 'Lỗi', 'error');
        } finally {
            setSavingRevisit(false);
        }
    };

    const filteredMedicines = medicines.filter(m => 
        medicineSearch.trim() && (
            m.name.toLowerCase().includes(medicineSearch.toLowerCase()) ||
            m.code.toLowerCase().includes(medicineSearch.toLowerCase())
        )
    );

    if (loading) {
        return (
            <div style={{ textAlign: 'center', padding: '80px 0', color: '#64748b' }}>
                <RefreshCw size={36} className="animate-spin" style={{ margin: '0 auto 16px auto', color: '#0284c7' }} />
                <h3 style={{ margin: 0, fontWeight: 700, color: '#0f172a' }}>Đang nạp hồ sơ khám bệnh...</h3>
                <p style={{ fontSize: '0.9rem', marginTop: '6px' }}>Vui lòng đợi giây lát trong khi hệ thống xác thực và tải dữ liệu bệnh nhân.</p>
            </div>
        );
    }

    if (!context) {
        return (
            <div className="card" style={{ padding: '40px', textAlign: 'center', margin: '40px auto', maxWidth: '500px' }}>
                <AlertCircle size={48} style={{ color: '#ef4444', margin: '0 auto 16px auto' }} />
                <h3>Không tìm thấy lịch hẹn</h3>
                <p style={{ color: '#64748b', fontSize: '0.9rem' }}>Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý của bạn.</p>
                <Link to="/doctor" className="btn-primary" style={{ display: 'inline-block', marginTop: '16px' }}>Quay lại bàn làm việc</Link>
            </div>
        );
    }

    const patient = context;
    const apt = context.currentAppointment;
    const isCompleted = apt?.status === 'Completed';

    return (
        <div style={{ paddingBottom: '90px' }}>
            {/* Top Navigation Bar */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                <Link to="/doctor" style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--c-primary)', textDecoration: 'none', fontWeight: 600, fontSize: '0.9rem' }}>
                    <ArrowLeft size={16} />
                    <span>Quay lại Bàn làm việc Bác sĩ</span>
                </Link>
                <div style={{ display: 'flex', gap: '8px' }}>
                    <button 
                        type="button"
                        onClick={loadContext} 
                        className="btn-secondary" 
                        style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '6px 12px', fontSize: '0.85rem' }}
                    >
                        <RefreshCw size={14} />
                        <span>Đồng bộ dữ liệu</span>
                    </button>
                </div>
            </div>

            {/* Read-only Banner if Completed */}
            {isCompleted && (
                <div style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '12px',
                    padding: '14px 18px',
                    backgroundColor: 'var(--c-warning-bg)',
                    border: '1px solid rgba(217, 119, 6, 0.3)',
                    borderRadius: 'var(--radius-lg)',
                    color: 'var(--c-warning)',
                    marginBottom: '20px'
                }}>
                    <AlertCircle size={20} style={{ flexShrink: 0 }} />
                    <div style={{ fontSize: '0.9rem', color: '#92400e' }}>
                        <strong>Ca khám này đã hoàn tất:</strong> Hồ sơ bệnh án và đơn thuốc đang ở trạng thái lưu trữ chính thức (chỉ đọc) để bảo toàn tính xác thực y khoa.
                    </div>
                </div>
            )}

            {/* Patient Header Spotlight Banner */}
            <div className="card" style={{ padding: '20px 24px', marginBottom: '20px', borderRadius: '10px', backgroundColor: '#f8fafc', borderLeft: '5px solid var(--c-primary)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
                    <div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                            <h2 style={{ margin: 0, fontSize: '1.4rem', fontWeight: 800, color: '#0f172a' }}>
                                {patient.patientName}
                            </h2>
                            <span style={{ backgroundColor: '#e0f2fe', color: '#0369a1', padding: '2px 8px', borderRadius: '4px', fontSize: '0.8rem', fontWeight: 700, fontFamily: 'monospace' }}>
                                #{apt.appointmentCode}
                            </span>
                            <span style={{ backgroundColor: isCompleted ? '#dcfce7' : '#e0e7ff', color: isCompleted ? '#15803d' : '#4338ca', padding: '2px 10px', borderRadius: '20px', fontSize: '0.8rem', fontWeight: 700 }}>
                                {isCompleted ? 'Hồ sơ đã hoàn tất' : 'Phiên khám lâm sàng'}
                            </span>
                        </div>

                        <div style={{ display: 'flex', gap: '20px', marginTop: '10px', flexWrap: 'wrap', fontSize: '0.9rem', color: '#475569' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <User size={16} style={{ color: '#64748b' }} />
                                <span>{patient.patientGender === 'Male' ? 'Nam' : patient.patientGender === 'Female' ? 'Nữ' : 'Khác'} {patient.patientDob ? `• NS: ${patient.patientDob}` : ''}</span>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Phone size={16} style={{ color: '#64748b' }} />
                                <span>{patient.patientPhone}</span>
                            </div>
                            {patient.address && (
                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <MapPin size={16} style={{ color: '#64748b' }} />
                                    <span>{patient.address}</span>
                                </div>
                            )}
                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Clock size={16} style={{ color: '#64748b' }} />
                                <span>Giờ hẹn: {apt.startTime.substring(0, 5)} - {apt.endTime.substring(0, 5)}</span>
                            </div>
                        </div>

                        <div style={{ marginTop: '10px', fontSize: '0.9rem', color: '#334155' }}>
                            <strong>Lý do đến khám:</strong> {apt.reason || 'Khám theo lịch'}
                        </div>
                    </div>

                    <div style={{ textAlign: 'right' }}>
                        <div style={{ fontSize: '0.8rem', color: '#64748b' }}>Lịch sử tại phòng khám</div>
                        <div style={{ fontSize: '1.2rem', fontWeight: 800, color: '#0f172a' }}>
                            {patient.totalPastVisits} lượt khám trước
                        </div>
                    </div>
                </div>
            </div>

            {/* Workspace Tab Navigation */}
            <div style={{ display: 'flex', borderBottom: '2px solid #e2e8f0', marginBottom: '20px', gap: '8px' }}>
                <button
                    onClick={() => setActiveTab('encounter')}
                    style={{
                        padding: '12px 18px',
                        border: 'none',
                        background: 'none',
                        fontWeight: 700,
                        fontSize: '0.95rem',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        color: activeTab === 'encounter' ? '#0284c7' : '#64748b',
                        borderBottom: activeTab === 'encounter' ? '3px solid #0284c7' : '3px solid transparent',
                        marginBottom: '-2px'
                    }}
                >
                    <Stethoscope size={18} />
                    <span>Diễn tiến lâm sàng</span>
                </button>

                <button
                    onClick={() => setActiveTab('vitals')}
                    style={{
                        padding: '12px 18px',
                        border: 'none',
                        background: 'none',
                        fontWeight: 700,
                        fontSize: '0.95rem',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        color: activeTab === 'vitals' ? '#0284c7' : '#64748b',
                        borderBottom: activeTab === 'vitals' ? '3px solid #0284c7' : '3px solid transparent',
                        marginBottom: '-2px'
                    }}
                >
                    <HeartPulse size={18} />
                    <span>Dấu hiệu sinh tồn {computedBmi ? `(BMI ${computedBmi})` : ''}</span>
                </button>

                <button
                    onClick={() => setActiveTab('prescription')}
                    style={{
                        padding: '12px 18px',
                        border: 'none',
                        background: 'none',
                        fontWeight: 700,
                        fontSize: '0.95rem',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        color: activeTab === 'prescription' ? '#0284c7' : '#64748b',
                        borderBottom: activeTab === 'prescription' ? '3px solid #0284c7' : '3px solid transparent',
                        marginBottom: '-2px'
                    }}
                >
                    <Pill size={18} />
                    <span>Kê đơn thuốc ({prescriptionItems.length})</span>
                </button>

                <button
                    onClick={() => setActiveTab('history')}
                    style={{
                        padding: '12px 18px',
                        border: 'none',
                        background: 'none',
                        fontWeight: 700,
                        fontSize: '0.95rem',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        color: activeTab === 'history' ? '#0284c7' : '#64748b',
                        borderBottom: activeTab === 'history' ? '3px solid #0284c7' : '3px solid transparent',
                        marginBottom: '-2px'
                    }}
                >
                    <History size={18} />
                    <span>Lịch sử khám ({patient.totalPastVisits})</span>
                </button>
            </div>

            {/* Tab 1: Clinical Encounter */}
            {activeTab === 'encounter' && (
                <div className="card" style={{ padding: '24px', borderRadius: '8px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '18px' }}>
                        <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 700, color: '#0f172a' }}>
                            Ghi nhận diễn tiến lâm sàng
                        </h3>
                        <button
                            type="button"
                            className="btn-secondary"
                            onClick={handleSaveEncounter}
                            disabled={savingEncounter}
                            style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}
                        >
                            <Save size={16} />
                            <span>{savingEncounter ? 'Đang lưu...' : 'Lưu nháp diễn tiến'}</span>
                        </button>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Triệu chứng chính / Lý do khám
                            </label>
                            <input
                                type="text"
                                value={chiefComplaint}
                                onChange={(e) => setChiefComplaint(e.target.value)}
                                placeholder="Ví dụ: Đau đầu, sốt nhẹ 2 ngày nay..."
                                style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: '3fr 1fr', gap: '10px' }}>
                            <div>
                                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                    Chẩn đoán bệnh *
                                </label>
                                <input
                                    type="text"
                                    value={diagnosis}
                                    onChange={(e) => setDiagnosis(e.target.value)}
                                    placeholder="Ví dụ: Viêm họng cấp / Tăng huyết áp độ 1..."
                                    required
                                    style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                />
                            </div>
                            <div>
                                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                    Mã ICD-10
                                </label>
                                <input
                                    type="text"
                                    value={diagnosisCode}
                                    onChange={(e) => setDiagnosisCode(e.target.value)}
                                    placeholder="J02.9"
                                    style={{ width: '100%', padding: '8px 10px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem', textTransform: 'uppercase' }}
                                />
                            </div>
                        </div>
                    </div>

                    <div style={{ marginBottom: '16px' }}>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                            Khám thực thể & Bệnh sử lâm sàng
                        </label>
                        <textarea
                            value={clinicalFindings}
                            onChange={(e) => setClinicalFindings(e.target.value)}
                            rows={4}
                            placeholder="Ghi nhận các triệu chứng cơ năng, thực thể: họng đỏ, không có giả mạc, tim phổi bình thường..."
                            style={{ width: '100%', padding: '10px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                        />
                    </div>

                    <div style={{ marginBottom: '16px' }}>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                            Hướng điều trị / Chỉ định
                        </label>
                        <textarea
                            value={treatmentPlan}
                            onChange={(e) => setTreatmentPlan(e.target.value)}
                            rows={3}
                            placeholder="Kế hoạch điều trị: Sử dụng kháng sinh, hạ sốt, uống nhiều nước ấm, nghỉ ngơi..."
                            style={{ width: '100%', padding: '10px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                        />
                    </div>

                    <div style={{ marginBottom: '16px' }}>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                            Tóm tắt kết luận buổi khám *
                        </label>
                        <textarea
                            value={summary}
                            onChange={(e) => setSummary(e.target.value)}
                            rows={3}
                            placeholder="Tóm tắt chẩn đoán và tình trạng chung của bệnh nhân..."
                            required
                            style={{ width: '100%', padding: '10px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                        />
                    </div>

                    <div>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                            Lời dặn dò & Lưu ý tái khám
                        </label>
                        <textarea
                            value={followUpInstruction}
                            onChange={(e) => setFollowUpInstruction(e.target.value)}
                            rows={2}
                            placeholder="Tái khám sau 5 ngày nếu không thuyên giảm hoặc có biểu hiện sốt cao liên tục..."
                            style={{ width: '100%', padding: '10px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                        />
                    </div>
                </div>
            )}

            {/* Tab 2: Vital Signs with computed BMI */}
            {activeTab === 'vitals' && (
                <div className="card" style={{ padding: '24px', borderRadius: '8px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                        <div>
                            <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 700, color: '#0f172a' }}>
                                Đo lường & Ghi nhận dấu hiệu sinh tồn
                            </h3>
                            <p style={{ margin: '4px 0 0 0', fontSize: '0.85rem', color: '#64748b' }}>
                                Tự động tính chỉ số BMI và phân loại theo tiêu chuẩn y tế quốc tế.
                            </p>
                        </div>
                        <button
                            type="button"
                            className="btn-secondary"
                            onClick={handleSaveVitals}
                            disabled={savingVitals}
                            style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}
                        >
                            <Save size={16} />
                            <span>{savingVitals ? 'Đang lưu...' : 'Lưu dấu hiệu sinh tồn'}</span>
                        </button>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '16px', marginBottom: '24px' }}>
                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Nhiệt độ (°C)
                            </label>
                            <input
                                type="number"
                                step="0.1"
                                min="30"
                                max="45"
                                value={temperature}
                                onChange={(e) => setTemperature(e.target.value)}
                                placeholder="37.0"
                                style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>

                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Huyết áp (Tâm thu / Tâm trương)
                            </label>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <input
                                    type="number"
                                    min="40"
                                    max="260"
                                    value={bpSystolic}
                                    onChange={(e) => setBpSystolic(e.target.value)}
                                    placeholder="120"
                                    style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                />
                                <span style={{ color: '#64748b', fontWeight: 700 }}>/</span>
                                <input
                                    type="number"
                                    min="30"
                                    max="180"
                                    value={bpDiastolic}
                                    onChange={(e) => setBpDiastolic(e.target.value)}
                                    placeholder="80"
                                    style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                />
                                <span style={{ fontSize: '0.8rem', color: '#64748b' }}>mmHg</span>
                            </div>
                        </div>

                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Nhịp tim / Mạch (nhịp/phút)
                            </label>
                            <input
                                type="number"
                                min="30"
                                max="220"
                                value={heartRate}
                                onChange={(e) => setHeartRate(e.target.value)}
                                placeholder="75"
                                style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>

                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Nhịp thở (lần/phút)
                            </label>
                            <input
                                type="number"
                                min="8"
                                max="60"
                                value={respiratoryRate}
                                onChange={(e) => setRespiratoryRate(e.target.value)}
                                placeholder="18"
                                style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>

                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Chiều cao (cm)
                            </label>
                            <input
                                type="number"
                                step="0.5"
                                min="30"
                                max="250"
                                value={height}
                                onChange={(e) => setHeight(e.target.value)}
                                placeholder="170"
                                style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>

                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Cân nặng (kg)
                            </label>
                            <input
                                type="number"
                                step="0.1"
                                min="2"
                                max="300"
                                value={weight}
                                onChange={(e) => setWeight(e.target.value)}
                                placeholder="65.0"
                                style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>

                        <div>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                Nồng độ oxy trong máu SpO2 (%)
                            </label>
                            <input
                                type="number"
                                min="50"
                                max="100"
                                value={spO2}
                                onChange={(e) => setSpO2(e.target.value)}
                                placeholder="98"
                                style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>
                    </div>

                    {/* Calculated BMI Badge Card */}
                    <div style={{ backgroundColor: '#f8fafc', padding: '18px 24px', borderRadius: '8px', border: '1px solid #e2e8f0', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '16px' }}>
                        <div>
                            <div style={{ fontSize: '0.85rem', color: '#64748b', fontWeight: 600 }}>CHỈ SỐ KHỐI CƠ THỂ (BMI) TỰ ĐỘNG</div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginTop: '6px' }}>
                                <span style={{ fontSize: '1.8rem', fontWeight: 800, color: '#0f172a' }}>
                                    {computedBmi !== null ? computedBmi : '--'}
                                </span>
                                {bmiClassification && (
                                    <span style={{ 
                                        backgroundColor: bmiClassification.color === '#15803d' ? '#dcfce7' : bmiClassification.color === '#d97706' ? '#fef3c7' : '#fee2e2',
                                        color: bmiClassification.color,
                                        fontWeight: 700,
                                        padding: '4px 12px',
                                        borderRadius: '20px',
                                        fontSize: '0.85rem'
                                    }}>
                                        {bmiClassification.label}
                                    </span>
                                )}
                            </div>
                        </div>

                        <div style={{ fontSize: '0.8rem', color: '#64748b', maxWidth: '360px' }}>
                            * Công thức: BMI = Cân nặng (kg) / [Chiều cao (m)]². Tiêu chuẩn: Gầy (&lt;18.5), Bình thường (18.5 - 24.9), Tiền béo phì (25 - 29.9), Béo phì (≥30).
                        </div>
                    </div>
                </div>
            )}

            {/* Tab 3: Prescription */}
            {activeTab === 'prescription' && (
                <div className="card" style={{ padding: '24px', borderRadius: '8px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '18px', flexWrap: 'wrap', gap: '12px' }}>
                        <div>
                            <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 700, color: '#0f172a' }}>
                                Kê đơn thuốc cho bệnh nhân
                            </h3>
                            <p style={{ margin: '4px 0 0 0', fontSize: '0.85rem', color: '#64748b' }}>
                                Tìm kiếm thuốc từ danh mục hoạt động của phòng khám và điều chỉnh liều dùng.
                            </p>
                        </div>
                        <button
                            type="button"
                            className="btn-secondary"
                            onClick={handleSavePrescriptionDraft}
                            disabled={savingPrescription}
                            style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}
                        >
                            <Save size={16} />
                            <span>{savingPrescription ? 'Đang lưu...' : 'Lưu nháp đơn thuốc'}</span>
                        </button>
                    </div>

                    {/* Medicine Search Box */}
                    <div style={{ position: 'relative', marginBottom: '20px' }}>
                        <div style={{ position: 'relative' }}>
                            <Search size={18} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: '#94a3b8' }} />
                            <input
                                type="text"
                                placeholder="Gõ tên thuốc hoặc mã thuốc để tìm kiếm và thêm vào đơn..."
                                value={medicineSearch}
                                onChange={(e) => setMedicineSearch(e.target.value)}
                                style={{ width: '100%', padding: '10px 12px 10px 40px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                            />
                        </div>

                        {/* Search Dropdown Results */}
                        {medicineSearch.trim() && (
                            <div style={{ 
                                position: 'absolute', top: '100%', left: 0, right: 0, zIndex: 10,
                                backgroundColor: 'white', border: '1px solid #cbd5e1', borderRadius: '6px',
                                boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)', maxHeight: '240px', overflowY: 'auto'
                            }}>
                                {filteredMedicines.length === 0 ? (
                                    <div style={{ padding: '12px', color: '#64748b', fontSize: '0.85rem', textAlign: 'center' }}>
                                        Không tìm thấy thuốc khớp với "{medicineSearch}" trong kho.
                                    </div>
                                ) : (
                                    filteredMedicines.map(med => (
                                        <div
                                            key={med.id}
                                            onClick={() => handleAddMedicine(med)}
                                            style={{
                                                padding: '10px 14px',
                                                display: 'flex',
                                                justifyContent: 'space-between',
                                                alignItems: 'center',
                                                cursor: 'pointer',
                                                borderBottom: '1px solid #f1f5f9',
                                                backgroundColor: 'white'
                                            }}
                                            onMouseEnter={(e) => (e.currentTarget.style.backgroundColor = '#f8fafc')}
                                            onMouseLeave={(e) => (e.currentTarget.style.backgroundColor = 'white')}
                                        >
                                            <div>
                                                <span style={{ fontWeight: 600, color: '#0f172a' }}>{med.name}</span>
                                                <span style={{ marginLeft: '8px', fontSize: '0.8rem', color: '#64748b' }}>
                                                    ({med.code}) • ĐVT: {med.unit}
                                                </span>
                                            </div>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                <span style={{ fontSize: '0.8rem', color: med.stockQuantity > 0 ? '#15803d' : '#dc2626', fontWeight: 600 }}>
                                                    Tồn: {med.stockQuantity} {med.unit}
                                                </span>
                                                <span style={{ backgroundColor: '#0284c7', color: 'white', padding: '2px 8px', borderRadius: '4px', fontSize: '0.75rem' }}>
                                                    + Thêm
                                                </span>
                                            </div>
                                        </div>
                                    ))
                                )}
                            </div>
                        )}
                    </div>

                    {/* Prescription Items Table */}
                    {prescriptionItems.length === 0 ? (
                        <div style={{ textAlign: 'center', padding: '36px 0', border: '1px dashed #cbd5e1', borderRadius: '6px', color: '#64748b' }}>
                            <Pill size={36} style={{ margin: '0 auto 10px auto', color: '#cbd5e1' }} />
                            <p style={{ margin: 0, fontWeight: 600 }}>Chưa có thuốc nào trong đơn.</p>
                            <p style={{ fontSize: '0.85rem', margin: '4px 0 0 0' }}>Sử dụng ô tìm kiếm phía trên để thêm thuốc vào đơn.</p>
                        </div>
                    ) : (
                        <div style={{ overflowX: 'auto', marginBottom: '20px' }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
                                <thead style={{ backgroundColor: '#f8fafc' }}>
                                    <tr style={{ borderBottom: '2px solid #e2e8f0', textAlign: 'left', color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>
                                        <th style={{ padding: '10px' }}>#</th>
                                        <th style={{ padding: '10px' }}>Tên thuốc</th>
                                        <th style={{ padding: '10px', width: '90px' }}>Số lượng</th>
                                        <th style={{ padding: '10px', width: '110px' }}>Liều dùng</th>
                                        <th style={{ padding: '10px', width: '140px' }}>Tần suất</th>
                                        <th style={{ padding: '10px', width: '90px' }}>Số ngày</th>
                                        <th style={{ padding: '10px' }}>Hướng dẫn uống</th>
                                        <th style={{ padding: '10px', width: '50px' }}></th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {prescriptionItems.map((item, idx) => (
                                        <tr key={item.medicineId} style={{ borderBottom: '1px solid #f1f5f9' }}>
                                            <td style={{ padding: '10px', fontWeight: 600, color: '#64748b' }}>{idx + 1}</td>
                                            <td style={{ padding: '10px' }}>
                                                <div style={{ fontWeight: 600, color: '#0f172a' }}>{item.medicineName}</div>
                                                <div style={{ fontSize: '0.75rem', color: '#64748b' }}>
                                                    {item.medicineCode} • {item.unit}
                                                </div>
                                            </td>
                                            <td style={{ padding: '10px' }}>
                                                <input
                                                    type="number"
                                                    min="1"
                                                    value={item.quantity}
                                                    onChange={(e) => handleItemChange(idx, 'quantity', parseInt(e.target.value, 10) || 1)}
                                                    style={{ width: '100%', padding: '6px', borderRadius: '4px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                                                />
                                            </td>
                                            <td style={{ padding: '10px' }}>
                                                <input
                                                    type="text"
                                                    value={item.dosage}
                                                    onChange={(e) => handleItemChange(idx, 'dosage', e.target.value)}
                                                    placeholder="1 viên"
                                                    style={{ width: '100%', padding: '6px', borderRadius: '4px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                                                />
                                            </td>
                                            <td style={{ padding: '10px' }}>
                                                <input
                                                    type="text"
                                                    value={item.frequency}
                                                    onChange={(e) => handleItemChange(idx, 'frequency', e.target.value)}
                                                    placeholder="Ngày 2 lần"
                                                    style={{ width: '100%', padding: '6px', borderRadius: '4px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                                                />
                                            </td>
                                            <td style={{ padding: '10px' }}>
                                                <input
                                                    type="number"
                                                    min="1"
                                                    max="90"
                                                    value={item.durationDays}
                                                    onChange={(e) => handleItemChange(idx, 'durationDays', parseInt(e.target.value, 10) || 1)}
                                                    style={{ width: '100%', padding: '6px', borderRadius: '4px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                                                />
                                            </td>
                                            <td style={{ padding: '10px' }}>
                                                <input
                                                    type="text"
                                                    value={item.instructions}
                                                    onChange={(e) => handleItemChange(idx, 'instructions', e.target.value)}
                                                    placeholder="Uống sau khi ăn 30 phút..."
                                                    style={{ width: '100%', padding: '6px', borderRadius: '4px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                                                />
                                            </td>
                                            <td style={{ padding: '10px', textAlign: 'center' }}>
                                                <button
                                                    type="button"
                                                    onClick={() => handleRemoveMedicine(idx)}
                                                    style={{ background: 'none', border: 'none', color: '#ef4444', cursor: 'pointer', padding: '4px' }}
                                                    title="Xóa thuốc khỏi đơn"
                                                >
                                                    <Trash2 size={16} />
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}

                    <div>
                        <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                            Ghi chú đơn thuốc cho dược sĩ / bệnh nhân
                        </label>
                        <textarea
                            value={prescriptionNotes}
                            onChange={(e) => setPrescriptionNotes(e.target.value)}
                            rows={2}
                            placeholder="Lưu ý dị ứng hoặc hướng dẫn bảo quản thuốc..."
                            style={{ width: '100%', padding: '8px 12px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                        />
                    </div>
                </div>
            )}

            {/* Tab 4: Past Visits History */}
            {activeTab === 'history' && (
                <div className="card" style={{ padding: '24px', borderRadius: '8px' }}>
                    <h3 style={{ margin: '0 0 16px 0', fontSize: '1.15rem', fontWeight: 700, color: '#0f172a' }}>
                        Lịch sử các lần khám trước của bệnh nhân ({patient.pastVisits.length} lượt)
                    </h3>

                    {patient.pastVisits.length === 0 ? (
                        <div style={{ textAlign: 'center', padding: '36px 0', color: '#64748b' }}>
                            <History size={36} style={{ margin: '0 auto 10px auto', color: '#cbd5e1' }} />
                            <p style={{ fontWeight: 600, margin: 0 }}>Đây là lần đầu bệnh nhân đến khám tại hệ thống phòng khám.</p>
                        </div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                            {patient.pastVisits.map(visit => (
                                <div key={visit.appointmentId} style={{ padding: '16px', borderRadius: '6px', backgroundColor: '#f8fafc', border: '1px solid #e2e8f0' }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px', flexWrap: 'wrap', gap: '6px' }}>
                                        <div>
                                            <span style={{ fontWeight: 700, color: '#0f172a', fontSize: '1rem' }}>
                                                Ngày: {visit.date}
                                            </span>
                                            <span style={{ marginLeft: '10px', fontSize: '0.85rem', color: '#64748b' }}>
                                                Mã: #{visit.appointmentCode}
                                            </span>
                                        </div>
                                        <div style={{ fontSize: '0.85rem', color: '#0369a1', fontWeight: 600 }}>
                                            BS: {visit.doctorName} • Khoa: {visit.specialtyName}
                                        </div>
                                    </div>

                                    {visit.diagnosis && (
                                        <div style={{ fontSize: '0.9rem', color: '#1e293b', marginBottom: '4px' }}>
                                            <strong>Chẩn đoán:</strong> {visit.diagnosis}
                                        </div>
                                    )}

                                    {visit.summary && (
                                        <div style={{ fontSize: '0.85rem', color: '#475569', marginBottom: '6px' }}>
                                            <strong>Kết luận:</strong> {visit.summary}
                                        </div>
                                    )}

                                    {visit.prescriptionItemNames && visit.prescriptionItemNames.length > 0 && (
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap', marginTop: '6px' }}>
                                            <span style={{ fontSize: '0.8rem', fontWeight: 600, color: '#475569' }}>Thuốc đã kê:</span>
                                            {visit.prescriptionItemNames.map((medName, mIdx) => (
                                                <span key={mIdx} style={{ backgroundColor: '#e0f2fe', color: '#0369a1', fontSize: '0.75rem', padding: '2px 8px', borderRadius: '4px' }}>
                                                    {medName}
                                                </span>
                                            ))}
                                        </div>
                                    )}
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            )}

            {/* Bottom Sticky Action Bar */}
            <div style={{ 
                position: 'fixed', bottom: 0, left: 0, right: 0, 
                backgroundColor: 'white', borderTop: '1px solid #e2e8f0', 
                padding: '14px 24px', zIndex: 100, 
                boxShadow: '0 -4px 6px -1px rgba(0,0,0,0.05)',
                display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '12px'
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <span style={{ fontSize: '0.85rem', color: '#64748b' }}>Trạng thái hồ sơ:</span>
                    <span style={{ 
                        backgroundColor: isCompleted ? '#dcfce7' : '#e0e7ff', 
                        color: isCompleted ? '#15803d' : '#4338ca', 
                        padding: '3px 8px', 
                        borderRadius: '4px', 
                        fontSize: '0.8rem', 
                        fontWeight: 700 
                    }}>
                        {isCompleted ? 'Đã hoàn tất' : 'Đang khám'}
                    </span>
                    {prescriptionItems.length > 0 && (
                        <span style={{ backgroundColor: '#f0fdf4', color: '#15803d', padding: '3px 8px', borderRadius: '4px', fontSize: '0.8rem', fontWeight: 600 }}>
                            Đơn thuốc ({prescriptionItems.length} loại)
                        </span>
                    )}
                </div>

                {isCompleted ? (
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#15803d', fontWeight: 600, fontSize: '0.9rem' }}>
                        <CheckCircle size={18} />
                        <span>Hồ sơ ca khám đã chốt và lưu trữ chính thức</span>
                    </div>
                ) : (
                    <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                        <button
                            type="button"
                            onClick={() => setIsRevisitModalOpen(true)}
                            className="btn-secondary"
                            style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}
                        >
                            <Calendar size={16} />
                            <span>Hẹn tái khám</span>
                        </button>

                        <button
                            type="button"
                            onClick={() => setIsCompleteModalOpen(true)}
                            className="btn-primary"
                            style={{ 
                                display: 'flex', alignItems: 'center', gap: '6px', 
                                padding: '10px 20px', borderRadius: '6px', 
                                fontWeight: 700, backgroundColor: '#059669', 
                                cursor: 'pointer', fontSize: '0.95rem' 
                            }}
                        >
                            <CheckCircle size={18} />
                            <span>HOÀN TẤT KHÁM BỆNH</span>
                        </button>
                    </div>
                )}
            </div>

            {/* Revisit Modal */}
            {isRevisitModalOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '16px' }}>
                    <div style={{ backgroundColor: 'white', borderRadius: '10px', width: '100%', maxWidth: '440px', padding: '24px', boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)' }}>
                        <h3 style={{ margin: '0 0 16px 0', fontSize: '1.2rem', fontWeight: 700, color: '#0f172a' }}>
                            Đề xuất tái khám cho bệnh nhân
                        </h3>
                        <form onSubmit={handleCreateRevisit}>
                            <div style={{ marginBottom: '14px' }}>
                                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                    Ngày hẹn tái khám đề xuất *
                                </label>
                                <input
                                    type="date"
                                    value={revisitDate}
                                    onChange={(e) => setRevisitDate(e.target.value)}
                                    min={minRevisitDate}
                                    required
                                    style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                />
                            </div>
                            <div style={{ marginBottom: '16px' }}>
                                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                    Ghi chú / Nhắc nhở tái khám
                                </label>
                                <textarea
                                    value={revisitNote}
                                    onChange={(e) => setRevisitNote(e.target.value)}
                                    placeholder="Tái khám đánh giá lại triệu chứng hoặc kết quả xét nghiệm..."
                                    rows={3}
                                    style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                />
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                <button type="button" onClick={() => setIsRevisitModalOpen(false)} className="btn-secondary" style={{ padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}>
                                    Hủy
                                </button>
                                <button type="submit" disabled={savingRevisit} className="btn-primary" style={{ padding: '8px 16px', borderRadius: '6px', cursor: 'pointer' }}>
                                    {savingRevisit ? 'Đang tạo...' : 'Xác nhận đề xuất'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Complete Consultation Confirmation Modal */}
            {isCompleteModalOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '16px' }}>
                    <div style={{ backgroundColor: 'white', borderRadius: '10px', width: '100%', maxWidth: '520px', padding: '24px', boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '16px' }}>
                            <div style={{ width: '40px', height: '40px', borderRadius: '50%', backgroundColor: '#dcfce7', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#15803d' }}>
                                <CheckCircle size={24} />
                            </div>
                            <div>
                                <h3 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 700, color: '#0f172a' }}>
                                    Xác nhận hoàn tất ca khám
                                </h3>
                                <p style={{ margin: '2px 0 0 0', fontSize: '0.85rem', color: '#64748b' }}>
                                    Giao dịch nguyên tử: Chốt hồ sơ bệnh án và phát hành đơn thuốc.
                                </p>
                            </div>
                        </div>

                        <div style={{ backgroundColor: '#f8fafc', padding: '14px', borderRadius: '6px', marginBottom: '16px', fontSize: '0.9rem', border: '1px solid #e2e8f0' }}>
                            <div style={{ marginBottom: '6px' }}>
                                <strong>Bệnh nhân:</strong> {patient.patientName} ({patient.patientPhone})
                            </div>
                            <div style={{ marginBottom: '6px' }}>
                                <strong>Chẩn đoán:</strong> {diagnosis || '<Chưa nhập>'}
                            </div>
                            <div style={{ marginBottom: '6px' }}>
                                <strong>Tóm tắt:</strong> {summary || '<Chưa nhập>'}
                            </div>
                            <div>
                                <strong>Đơn thuốc:</strong> {prescriptionItems.length > 0 ? `${prescriptionItems.length} loại thuốc` : 'Không kê đơn'}
                            </div>
                        </div>

                        {prescriptionItems.length > 0 && (
                            <div style={{ marginBottom: '16px' }}>
                                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '0.9rem', color: '#1e293b' }}>
                                    <input
                                        type="checkbox"
                                        checked={issuePrescriptionCheck}
                                        onChange={(e) => setIssuePrescriptionCheck(e.target.checked)}
                                        style={{ width: '16px', height: '16px', cursor: 'pointer' }}
                                    />
                                    <span>Chốt và phát hành đơn thuốc sang Dược sĩ (Trạng thái Issued)</span>
                                </label>
                            </div>
                        )}

                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                            <button 
                                type="button" 
                                onClick={() => setIsCompleteModalOpen(false)} 
                                className="btn-secondary" 
                                style={{ padding: '8px 16px', borderRadius: '6px', cursor: 'pointer' }}
                            >
                                Quay lại chỉnh sửa
                            </button>
                            <button 
                                type="button" 
                                onClick={handleCompleteConsultation} 
                                disabled={completing || !diagnosis.trim() || !summary.trim()} 
                                className="btn-primary" 
                                style={{ padding: '8px 20px', borderRadius: '6px', backgroundColor: '#059669', fontWeight: 700, cursor: 'pointer' }}
                            >
                                {completing ? 'Đang hoàn tất...' : 'Xác nhận hoàn tất'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
