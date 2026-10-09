import { toLocalDateString } from '../../utils/formatters';
import React, { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { Button, Card, Col, Form, Input, Row } from 'antd';
import {
    Stethoscope, HeartPulse, Pill, History, Save, CheckCircle,
    AlertCircle, ArrowLeft, Trash2, Search, Clock, Calendar,
    User, Phone, MapPin, RefreshCw, FlaskConical, Printer,
    Check, AlertTriangle
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import { diagnosticApi } from '../../api/diagnosticApi';
import type {
    PatientClinicalContextDto,
    SaveEncounterRequest,
    SaveVitalSignsRequest,
    SavePrescriptionDraftRequest,
    CompleteConsultationRequest,
    DiagnosticServiceDto,
    DiagnosticOrderDto
} from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { useCopilotResource } from '../../components/copilot/copilotResourceContext';
import { LoadingState, InlineError, DataTable, EmptyState, StatusBadge } from '../../components/common';
import tabStyles from './DoctorExaminationWorkspace.module.css';

interface ActiveMedicine {
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
}

export const DoctorExaminationWorkspace: React.FC = () => {
    const { id, visitId } = useParams<{ id?: string; visitId?: string }>();
    const isVisit = Boolean(visitId);
    const currentId = Number(visitId || id);
    const appointmentId = isVisit ? 0 : Number(id);
    const navigate = useNavigate();
    const { showAlert, showToast } = useDialog();
    const { setSelection } = useCopilotResource();

    const [context, setContext] = useState<PatientClinicalContextDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [activeTab, setActiveTab] = useState<'encounter' | 'vitals' | 'diagnostics' | 'prescription' | 'history'>('encounter');

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

    // Tab 3: Diagnostic Orders Form & State
    const [diagnosticCatalog, setDiagnosticCatalog] = useState<DiagnosticServiceDto[]>([]);
    const [selectedServiceIds, setSelectedServiceIds] = useState<number[]>([]);
    const [clinicalIndication, setClinicalIndication] = useState('');
    const [orderNotes, setOrderNotes] = useState('');
    const [diagnosticOrders, setDiagnosticOrders] = useState<DiagnosticOrderDto[]>([]);
    const [loadingOrders, setLoadingOrders] = useState(false);
    const [creatingOrder, setCreatingOrder] = useState(false);
    const [actionOrderId, setActionOrderId] = useState<number | null>(null);
    const [catalogCategory, setCatalogCategory] = useState<string>('All');
    const [pollError, setPollError] = useState(false);
    const inFlightPollRef = useRef(false);
    const pollSeqRef = useRef(0);

    // Tab 4: Prescription Form
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

    const isMountedRef = useRef(true);
    const activeIdRef = useRef(currentId);
    activeIdRef.current = currentId;

    useEffect(() => {
        isMountedRef.current = true;
        return () => {
            isMountedRef.current = false;
        };
    }, []);

    // Reset orders and in-flight states only when visit actually changes (not on mount)
    const prevIdRef = useRef(currentId);
    useEffect(() => {
        if (prevIdRef.current !== currentId) {
            prevIdRef.current = currentId;
            setDiagnosticOrders([]);
            setPollError(false);
            pollSeqRef.current++;
            inFlightPollRef.current = false;
        }
    }, [currentId]);

    // Unified load and polling for diagnostic orders
    const loadDiagnosticOrders = useCallback(async (isBackground: boolean = false) => {
        if (!currentId) return;
        if (isBackground && inFlightPollRef.current) return;

        const targetId = currentId;
        inFlightPollRef.current = true;
        const currentSeq = ++pollSeqRef.current;
        if (!isBackground) {
            setLoadingOrders(true);
        }

        try {
            const res = isVisit
                ? await diagnosticApi.getDoctorOrdersByVisit(targetId)
                : await diagnosticApi.getDoctorOrdersByAppointment(appointmentId);

            if (currentSeq === pollSeqRef.current && activeIdRef.current === targetId && isMountedRef.current) {
                if (res.success && res.data) {
                    setDiagnosticOrders(res.data);
                    setPollError(false);
                } else {
                    setPollError(true);
                }
            }
        } catch {
            if (currentSeq === pollSeqRef.current && activeIdRef.current === targetId && isMountedRef.current) {
                setPollError(true);
            }
        } finally {
            if (currentSeq === pollSeqRef.current) {
                inFlightPollRef.current = false;
                if (!isBackground && isMountedRef.current) {
                    setLoadingOrders(false);
                }
            }
        }
    }, [currentId, isVisit, appointmentId]);

    // Load diagnostic catalog
    const loadDiagnosticCatalog = useCallback(async () => {
        try {
            const res = await diagnosticApi.getCatalog();
            if (res.success && res.data) {
                setDiagnosticCatalog(res.data);
            }
        } catch {
            // Ignored
        }
    }, []);

    // Load initial context
    const loadContext = useCallback(async () => {
        if (!currentId) return;
        const targetId = currentId;
        try {
            const ctxPromise = isVisit
                ? doctorApi.getVisitPatientClinicalContext(targetId)
                : doctorApi.getPatientClinicalContext(appointmentId);

            const [ctxRes, medRes] = await Promise.all([
                ctxPromise,
                doctorApi.getActiveMedicines(),
                loadDiagnosticOrders(),
                loadDiagnosticCatalog()
            ]);

            if (activeIdRef.current === targetId && isMountedRef.current) {
                if (ctxRes.success && ctxRes.data) {
                    const data = ctxRes.data;
                    setContext(data);

                    // Populate Encounter
                    if (data.encounter) {
                        setChiefComplaint(data.encounter.chiefComplaint || data.currentAppointment?.reason || '');
                        setClinicalFindings(data.encounter.clinicalFindings || '');
                        setDiagnosis(data.encounter.diagnosis || '');
                        setDiagnosisCode(data.encounter.diagnosisCode || '');
                        setTreatmentPlan(data.encounter.treatmentPlan || '');
                        setSummary(data.encounter.summary || '');
                        setFollowUpInstruction(data.encounter.followUpInstruction || '');
                        setEncounterRowVersion(data.encounter.rowVersion || null);
                    } else {
                        setChiefComplaint(data.currentAppointment?.reason || '');
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
                            availableStock: 999,
                            quantity: i.quantity,
                            dosage: i.dosage || '',
                            frequency: i.frequency || '',
                            durationDays: i.durationDays || 5,
                            instructions: i.instructions || ''
                        })));
                    }
                }

                if (medRes.success && medRes.data) {
                    setMedicines(medRes.data);
                }
            }
        } catch (err: any) {
            if (activeIdRef.current === targetId && isMountedRef.current) {
                showAlert(err.response?.data?.message || 'Không thể tải hồ sơ khám lâm sàng của bệnh nhân.', 'Lỗi', 'error');
            }
        } finally {
            if (activeIdRef.current === targetId && isMountedRef.current) {
                setLoading(false);
            }
        }
    }, [currentId, isVisit, appointmentId, showAlert, loadDiagnosticOrders, loadDiagnosticCatalog]);

    useEffect(() => {
        let isMounted = true;
        const init = async () => {
            if (!isMounted) return;
            await loadContext();
        };
        void init();
        return () => {
            isMounted = false;
        };
    }, [loadContext]);

    useEffect(() => {
        const refresh = () => { void loadContext(); };
        window.addEventListener('cliniccare:copilot-action-completed', refresh);
        return () => window.removeEventListener('cliniccare:copilot-action-completed', refresh);
    }, [loadContext]);

    useEffect(() => {
        if (!currentId) {
            setSelection(null);
            return;
        }
        setSelection({
            context: {
                ...(isVisit ? { visitId: currentId } : { appointmentId: currentId }),
                serviceIds: selectedServiceIds
            },
            source: isVisit ? 'doctor-visit-examination' : 'doctor-appointment-examination',
            label: 'Ca khám đang mở',
            resourceVersion: encounterRowVersion,
            actionArguments: {
                clinicalIndication: clinicalIndication.trim(),
                note: orderNotes.trim() || undefined,
                notes: prescriptionNotes.trim() || undefined,
                items: prescriptionItems.map(item => ({
                    medicineId: item.medicineId,
                    quantity: item.quantity,
                    dosage: item.dosage.trim() || undefined,
                    frequency: item.frequency.trim() || undefined,
                    durationDays: item.durationDays,
                    instructions: item.instructions.trim() || undefined
                }))
            }
        });
    }, [currentId, isVisit, selectedServiceIds, clinicalIndication, orderNotes, prescriptionNotes, prescriptionItems, encounterRowVersion, setSelection]);

    // Background polling for diagnostic orders (every 20s) to reflect lab/imaging results in real-time
    useEffect(() => {
        if (!currentId) return;

        const timer = setInterval(() => {
            void loadDiagnosticOrders(true);
        }, 20000);

        return () => {
            pollSeqRef.current++;
            inFlightPollRef.current = false;
            clearInterval(timer);
        };
    }, [currentId, loadDiagnosticOrders]);

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
        return toLocalDateString(tomorrow);
    }, []);

    // Previous height for reuse
    const previousHeight = useMemo(() => {
        return context?.latestKnownVitals?.height || context?.anthropometricComparison?.previousMeasurement?.height || null;
    }, [context]);

    // Diagnostic completion guards
    const pendingDiagnosticOrder = useMemo(() => {
        return diagnosticOrders.find(o => o.status === 'Ordered' || o.status === 'InProgress');
    }, [diagnosticOrders]);

    const unreviewedDiagnosticOrder = useMemo(() => {
        return diagnosticOrders.find(o => o.status === 'Completed' && !o.reviewedAtUtc);
    }, [diagnosticOrders]);

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
            const res = isVisit
                ? await doctorApi.saveVisitEncounter(currentId, req)
                : await doctorApi.saveEncounter(appointmentId, req);
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
            const res = isVisit
                ? await doctorApi.saveVisitVitalSigns(currentId, req)
                : await doctorApi.saveVitalSigns(appointmentId, req);
            if (res.success && res.data) {
                setVitalsRowVersion(res.data.rowVersion || null);
                showToast('Đã lưu dấu hiệu sinh tồn thành công.', 'success');
                // Refresh context to reload updated longitudinal comparison deltas
                const ctxRes = isVisit
                    ? await doctorApi.getVisitPatientClinicalContext(currentId)
                    : await doctorApi.getPatientClinicalContext(appointmentId);
                if (ctxRes.success && ctxRes.data) {
                    setContext(ctxRes.data);
                }
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

    // Handlers: Diagnostic Orders
    const handleToggleSelectService = (serviceId: number) => {
        setSelectedServiceIds(prev => 
            prev.includes(serviceId) ? prev.filter(id => id !== serviceId) : [...prev, serviceId]
        );
    };

    const handleCreateDiagnosticOrder = async (e: React.FormEvent) => {
        e.preventDefault();
        if (selectedServiceIds.length === 0) {
            showAlert('Vui lòng chọn ít nhất một dịch vụ cận lâm sàng để chỉ định.', 'Chưa chọn dịch vụ', 'warning');
            return;
        }

        setCreatingOrder(true);
        try {
            const res = isVisit
                ? await diagnosticApi.createDoctorOrderForVisit(currentId, {
                    serviceIds: selectedServiceIds,
                    clinicalIndication: clinicalIndication.trim() || 'Chỉ định cận lâm sàng',
                    note: orderNotes.trim() || undefined
                })
                : await diagnosticApi.createDoctorOrder(appointmentId, {
                    serviceIds: selectedServiceIds,
                    clinicalIndication: clinicalIndication.trim() || 'Chỉ định cận lâm sàng',
                    note: orderNotes.trim() || undefined
                });

            if (res.success) {
                showToast('Đã tạo phiếu chỉ định cận lâm sàng thành công!', 'success');
                setSelectedServiceIds([]);
                setClinicalIndication('');
                setOrderNotes('');
                await loadDiagnosticOrders();
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tạo phiếu chỉ định cận lâm sàng.', 'Lỗi', 'error');
        } finally {
            setCreatingOrder(false);
        }
    };

    const handleReviewOrder = async (orderId: number) => {
        setActionOrderId(orderId);
        try {
            const res = await diagnosticApi.reviewDoctorOrder(orderId);
            if (res.success) {
                showToast('Đã xác nhận xem kết quả cận lâm sàng.', 'success');
                await loadDiagnosticOrders();
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể xác nhận kết quả.', 'Lỗi', 'error');
        } finally {
            setActionOrderId(null);
        }
    };

    const handleCancelOrder = async (orderId: number) => {
        setActionOrderId(orderId);
        try {
            const res = await diagnosticApi.cancelDoctorOrder(orderId, { reason: 'Bác sĩ hủy chỉ định.' });
            if (res.success) {
                showToast('Đã hủy phiếu chỉ định cận lâm sàng.', 'info');
                await loadDiagnosticOrders();
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể hủy phiếu chỉ định.', 'Lỗi', 'error');
        } finally {
            setActionOrderId(null);
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
            const res = isVisit
                ? await doctorApi.saveVisitPrescriptionDraft(currentId, req)
                : await doctorApi.savePrescriptionDraft(appointmentId, req);
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

    // Handle Complete Consultation with Diagnostic Guards
    const handleOpenCompleteModal = () => {
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

        if (pendingDiagnosticOrder) {
            showAlert(
                `Phiếu chỉ định "${pendingDiagnosticOrder.orderCode}" đang ở trạng thái ${pendingDiagnosticOrder.status === 'Ordered' ? 'Chờ thực hiện' : 'Đang thực hiện'}. Bạn chỉ có thể hoàn tất ca khám sau khi có kết quả hoặc hủy phiếu chỉ định nếu không còn nhu cầu thực hiện.`,
                'Chưa thể hoàn tất ca khám',
                'warning'
            );
            setActiveTab('diagnostics');
            return;
        }

        if (unreviewedDiagnosticOrder) {
            showAlert(
                `Phiếu chỉ định "${unreviewedDiagnosticOrder.orderCode}" đã có kết quả cận lâm sàng nhưng chưa được bác sĩ bấm xác nhận đã xem kết quả. Vui lòng kiểm tra và bấm "Xác nhận đã xem kết quả" trước khi hoàn tất ca khám.`,
                'Chưa thể hoàn tất ca khám',
                'warning'
            );
            setActiveTab('diagnostics');
            return;
        }

        setIsCompleteModalOpen(true);
    };

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

        if (pendingDiagnosticOrder) {
            showAlert(
                `Phiếu chỉ định "${pendingDiagnosticOrder.orderCode}" đang ở trạng thái ${pendingDiagnosticOrder.status === 'Ordered' ? 'Chờ thực hiện' : 'Đang thực hiện'}. Bạn chỉ có thể hoàn tất ca khám sau khi có kết quả hoặc hủy phiếu chỉ định.`,
                'Chưa thể hoàn tất ca khám',
                'warning'
            );
            setIsCompleteModalOpen(false);
            setActiveTab('diagnostics');
            return;
        }

        if (unreviewedDiagnosticOrder) {
            showAlert(
                `Phiếu chỉ định "${unreviewedDiagnosticOrder.orderCode}" đã có kết quả nhưng bác sĩ chưa xác nhận đã xem. Vui lòng bấm "Xác nhận đã xem kết quả" trước.`,
                'Chưa thể hoàn tất ca khám',
                'warning'
            );
            setIsCompleteModalOpen(false);
            setActiveTab('diagnostics');
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

            const res = isVisit
                ? await doctorApi.completeVisitConsultation(currentId, req)
                : await doctorApi.completeConsultation(appointmentId, req);
            if (res.success) {
                showToast('Đã hoàn tất ca khám lâm sàng và cấp hồ sơ bệnh án thành công!', 'success');
                setIsCompleteModalOpen(false);
                if (isVisit) {
                    navigate('/doctor/queue');
                } else {
                    navigate(`/doctor/appointments/${appointmentId}`);
                }
            }
        } catch (err: any) {
            if (err.response?.status === 409) {
                showAlert('Hồ sơ bệnh án đã bị sửa đổi đồng thời bởi một phiên khác. Vui lòng tải lại và kiểm tra.', 'Xung đột dữ liệu', 'error');
                await loadContext();
            } else if (err.response?.data?.errorCode === 'PENDING_DIAGNOSTIC_RESULTS' || err.response?.data?.errorCode === 'UNREVIEWED_DIAGNOSTIC_RESULTS') {
                showAlert(err.response?.data?.message || 'Chưa thể kết thúc ca khám do chưa hoàn tất quy trình cận lâm sàng.', 'Chặn hoàn tất khám', 'warning');
                setActiveTab('diagnostics');
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

        if (isVisit && !appointmentId) {
            showAlert('Đề xuất tái khám trực tuyến hiện áp dụng cho bệnh nhân có lịch hẹn trước. Bác sĩ vui lòng dặn dò ngày tái khám tại mục Lời dặn / Diễn tiến khám.', 'Thông báo', 'info');
            setIsRevisitModalOpen(false);
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

    const filteredCatalog = useMemo(() => {
        if (catalogCategory === 'All') return diagnosticCatalog;
        return diagnosticCatalog.filter(s => s.category === catalogCategory);
    }, [diagnosticCatalog, catalogCategory]);

    const categoryMap: Record<string, string> = {
        Laboratory: 'Xét nghiệm',
        Ultrasound: 'Siêu âm',
        Imaging: 'CĐ Hình ảnh',
        Other: 'Khác'
    };

    const statusBadge = (status: string) => {
        switch (status) {
            case 'Ordered':
                return <StatusBadge status="Pending" label="Chờ thực hiện" />;
            case 'InProgress':
                return <StatusBadge status="InConsultation" label="Đang thực hiện" />;
            case 'Completed':
                return <StatusBadge status={status} label="Đã hoàn tất" />;
            case 'Cancelled':
                return <StatusBadge status={status} label="Đã hủy" />;
            default:
                return <StatusBadge status={status} label={status} />;
        }
    };

    if (loading) {
        return <LoadingState message="Đang nạp hồ sơ khám bệnh..." height="300px" />;
    }

    if (!context) {
        return (
            <Card className={tabStyles.missingAppointment}>
                <AlertCircle size={48} className={tabStyles.detail23} />
                <h3>Không tìm thấy lịch hẹn</h3>
                <p className={tabStyles.description2}>Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý của bạn.</p>
                <Link to="/doctor">
                    <Button type="primary" className={tabStyles.sectionSpacing6}>Quay lại bàn làm việc</Button>
                </Link>
            </Card>
        );
    }

    const patient = context;
    const apt = context.currentAppointment;
    const isCompleted = apt?.status === 'Completed';
    const comparison = context.anthropometricComparison;
    const prevMeasurement = comparison?.previousMeasurement;
    const historyList = context.vitalHistory || [];

    return (
        <div className={tabStyles.workspace}>
            {/* Top Navigation Bar */}
            <div className={tabStyles.sectionHeader}>
                <Link to="/doctor" className={tabStyles.backLink}>
                    <ArrowLeft size={16} />
                    <span>Quay lại Bàn làm việc Bác sĩ</span>
                </Link>
                <Button size="small" icon={<RefreshCw size={14} />} onClick={loadContext}>
                    Đồng bộ dữ liệu
                </Button>
            </div>

            {/* Read-only Banner if Completed */}
            {isCompleted && (
                <div className={tabStyles.inlineGroup12}>
                    <AlertCircle size={20} className={tabStyles.detail25} />
                    <div className={tabStyles.detailText35}>
                        <strong>Ca khám này đã hoàn tất:</strong> Hồ sơ bệnh án và đơn thuốc đang ở trạng thái lưu trữ chính thức (chỉ đọc) để bảo toàn tính xác thực y khoa.
                    </div>
                </div>
            )}

            {/* Pending or Unreviewed Diagnostic Warning Banner */}
            {!isCompleted && pendingDiagnosticOrder && (
                <div className={tabStyles.inlineGroup13}>
                    <AlertTriangle size={20} className={tabStyles.detail26} />
                    <div className={tabStyles.detail27}>
                        <strong>Chỉ định CLS đang chờ:</strong> Phiếu <code>{pendingDiagnosticOrder.orderCode}</code> đang được kỹ thuật viên tiếp nhận/thực hiện. Ca khám chưa thể kết thúc cho đến khi có kết quả đầy đủ.
                    </div>
                </div>
            )}

            {!isCompleted && !pendingDiagnosticOrder && unreviewedDiagnosticOrder && (
                <div className={tabStyles.navigation}>
                    <div className={tabStyles.inlineGroup2}>
                        <CheckCircle size={20} className={tabStyles.detail28} />
                        <div className={tabStyles.detail27}>
                            <strong>Đã có kết quả CLS:</strong> Phiếu <code>{unreviewedDiagnosticOrder.orderCode}</code> đã có kết quả. Vui lòng chuyển sang tab Cận lâm sàng và bấm "Xác nhận đã xem kết quả" để hoàn tất ca khám.
                        </div>
                    </div>
                    <Button type="primary" size="small" onClick={() => setActiveTab('diagnostics')}>
                        Xem kết quả ngay
                    </Button>
                </div>
            )}

            {/* Patient Header Spotlight Banner */}
            <Card className={[tabStyles.tabPanel, tabStyles.sectionSpacing7].join(' ')} >
                <div className={tabStyles.sectionHeader10}>
                    <div>
                        <div className={tabStyles.inlineGroup14}>
                            <h2 className={tabStyles.detailText36}>
                                {patient.patientName}
                            </h2>
                            <span className={tabStyles.detailText37}>
                                #{apt.appointmentCode}
                            </span>
                            <span className={[tabStyles.detail29, (isCompleted ? tabStyles.statusBackground : tabStyles.statusBackground8), (isCompleted ? tabStyles.statusText3 : tabStyles.statusText6)].join(' ')}>
                                {isCompleted ? 'Hồ sơ đã hoàn tất' : 'Phiên khám lâm sàng'}
                            </span>
                        </div>

                        <div className={tabStyles.patientMetadata}>
                            <div className={tabStyles.inlineGroup2}>
                                <User size={16} className={tabStyles.statusText5} />
                                <span>{patient.patientGender === 'Male' ? 'Nam' : patient.patientGender === 'Female' ? 'Nữ' : 'Khác'} {patient.patientDob ? `• NS: ${patient.patientDob}` : ''}</span>
                            </div>
                            <div className={tabStyles.inlineGroup2}>
                                <Phone size={16} className={tabStyles.statusText5} />
                                <span>{patient.patientPhone}</span>
                            </div>
                            {patient.address && (
                                <div className={tabStyles.inlineGroup2}>
                                    <MapPin size={16} className={tabStyles.statusText5} />
                                    <span>{patient.address}</span>
                                </div>
                            )}
                            {apt.startTime && (
                                <div className={tabStyles.inlineGroup2}>
                                    <Clock size={16} className={tabStyles.statusText5} />
                                    <span>Giờ hẹn: {apt.startTime.substring(0, 5)} - {apt.endTime ? apt.endTime.substring(0, 5) : ''}</span>
                                </div>
                            )}
                        </div>

                        <div className={tabStyles.detailText38}>
                            <strong>Lý do đến khám:</strong> {apt.reason || 'Khám theo lịch'}
                        </div>
                    </div>

                    <div className={tabStyles.detail30}>
                        <div className={tabStyles.detailText9}>Lịch sử tại phòng khám</div>
                        <div className={tabStyles.detailText39}>
                            {patient.totalPastVisits} lượt khám trước
                        </div>
                    </div>
                </div>
            </Card>

            {/* Workspace Tab Navigation */}
            <div className={tabStyles.tabBar}>
                <button
                    type="button"
                    onClick={() => setActiveTab('encounter')}
                    className={`${tabStyles.tabButton} ${activeTab === 'encounter' ? tabStyles.tabButtonActive : ''}`}
                >
                    <Stethoscope size={18} />
                    <span>Diễn tiến lâm sàng</span>
                </button>

                <button
                    type="button"
                    onClick={() => setActiveTab('vitals')}
                    className={`${tabStyles.tabButton} ${activeTab === 'vitals' ? tabStyles.tabButtonActive : ''}`}
                >
                    <HeartPulse size={18} />
                    <span>Dấu hiệu sinh tồn {computedBmi ? `(BMI ${computedBmi})` : ''}</span>
                </button>

                <button
                    type="button"
                    onClick={() => setActiveTab('diagnostics')}
                    className={`${tabStyles.tabButton} ${activeTab === 'diagnostics' ? tabStyles.tabButtonActive : ''}`}
                >
                    <FlaskConical size={18} />
                    <span>Chỉ định Cận lâm sàng ({diagnosticOrders.length})</span>
                </button>

                <button
                    type="button"
                    onClick={() => setActiveTab('prescription')}
                    className={`${tabStyles.tabButton} ${activeTab === 'prescription' ? tabStyles.tabButtonActive : ''}`}
                >
                    <Pill size={18} />
                    <span>Kê đơn thuốc ({prescriptionItems.length})</span>
                </button>

                <button
                    type="button"
                    onClick={() => setActiveTab('history')}
                    className={`${tabStyles.tabButton} ${activeTab === 'history' ? tabStyles.tabButtonActive : ''}`}
                >
                    <History size={18} />
                    <span>Lịch sử khám ({patient.totalPastVisits})</span>
                </button>
            </div>

            {/* Tab 1: Clinical Encounter */}
            {activeTab === 'encounter' && (
                <Card className={[tabStyles.tabPanel].join(' ')} >
                    <div className={tabStyles.sectionHeader}>
                        <h3 className={tabStyles.sectionTitle}>
                            Ghi nhận diễn tiến lâm sàng
                        </h3>
                        <Button
                            htmlType="button"
                            className={[tabStyles.actionButton].join(' ')}
                            onClick={handleSaveEncounter}
                            disabled={savingEncounter}
                        >
                            <Save size={16} />
                            <span>{savingEncounter ? 'Đang lưu...' : 'Lưu nháp diễn tiến'}</span>
                        </Button>
                    </div>

                    <Row className={[tabStyles.responsiveRow, tabStyles.sectionSpacing].join(' ')}>
                        <Col xs={24} md={12} className={tabStyles.column}>
                            <Form.Item className={tabStyles.formItem}>
                                <label className={tabStyles.fieldLabel}>
                                    Triệu chứng chính / Lý do khám
                                </label>
                                <Input
                                    type="text"
                                    value={chiefComplaint}
                                    onChange={(e) => setChiefComplaint(e.target.value)}
                                    placeholder="Ví dụ: Đau đầu, sốt nhẹ 2 ngày nay..."
                                    className={[tabStyles.fieldControl].join(' ')}
                                />
                            </Form.Item>
                        </Col>

                        <Col xs={24} md={12} className={tabStyles.column}>
                            <Row className={tabStyles.responsiveRow}>
                                <Col xs={24} md={18} className={tabStyles.column}>
                                    <Form.Item className={tabStyles.formItem}>
                                        <label className={tabStyles.fieldLabel}>
                                            Chẩn đoán bệnh *
                                        </label>
                                        <Input
                                            type="text"
                                            value={diagnosis}
                                            onChange={(e) => setDiagnosis(e.target.value)}
                                            placeholder="Ví dụ: Viêm họng cấp / Tăng huyết áp độ 1..."
                                            required
                                            className={[tabStyles.fieldControl].join(' ')}
                                        />
                                    </Form.Item>
                                </Col>
                                <Col xs={24} md={6} className={tabStyles.column}>
                                    <Form.Item className={tabStyles.formItem}>
                                        <label className={tabStyles.fieldLabel}>
                                            Mã ICD-10
                                        </label>
                                        <Input
                                            type="text"
                                            value={diagnosisCode}
                                            onChange={(e) => setDiagnosisCode(e.target.value)}
                                            placeholder="J02.9"
                                            className={[tabStyles.fieldControl, tabStyles.icdCode].join(' ')}
                                        />
                                    </Form.Item>
                                </Col>
                            </Row></Col>
                    </Row>

                    <Form.Item className={tabStyles.formItem}>
                        <label className={tabStyles.fieldLabel}>
                            Khám thực thể & Bệnh sử lâm sàng
                        </label>
                        <Input.TextArea
                            value={clinicalFindings}
                            onChange={(e) => setClinicalFindings(e.target.value)}
                            rows={4}
                            placeholder="Ghi nhận các triệu chứng cơ năng, thực thể: họng đỏ, không có giả mạc, tim phổi bình thường..."
                            className={[tabStyles.fieldControl].join(' ')}
                        />
                    </Form.Item>

                    <Form.Item className={tabStyles.formItem}>
                        <label className={tabStyles.fieldLabel}>
                            Hướng điều trị / Chỉ định
                        </label>
                        <Input.TextArea
                            value={treatmentPlan}
                            onChange={(e) => setTreatmentPlan(e.target.value)}
                            rows={3}
                            placeholder="Kế hoạch điều trị: Sử dụng kháng sinh, hạ sốt, uống nhiều nước ấm, nghỉ ngơi..."
                            className={[tabStyles.fieldControl].join(' ')}
                        />
                    </Form.Item>

                    <Form.Item className={tabStyles.formItem}>
                        <label className={tabStyles.fieldLabel}>
                            Tóm tắt kết luận buổi khám *
                        </label>
                        <Input.TextArea
                            value={summary}
                            onChange={(e) => setSummary(e.target.value)}
                            rows={3}
                            placeholder="Tóm tắt chẩn đoán và tình trạng chung của bệnh nhân..."
                            required
                            className={[tabStyles.fieldControl].join(' ')}
                        />
                    </Form.Item>

                    <Form.Item className={tabStyles.formItem}>
                        <label className={tabStyles.fieldLabel}>
                            Lời dặn dò & Lưu ý tái khám
                        </label>
                        <Input.TextArea
                            value={followUpInstruction}
                            onChange={(e) => setFollowUpInstruction(e.target.value)}
                            rows={2}
                            placeholder="Tái khám sau 5 ngày nếu không thuyên giảm hoặc có biểu hiện sốt cao liên tục..."
                            className={[tabStyles.fieldControl].join(' ')}
                        />
                    </Form.Item>
                </Card>
            )}

            {/* Tab 2: Longitudinal Vital Signs with 3 Distinct Regions */}
            {activeTab === 'vitals' && (
                <Card className={[tabStyles.tabPanel, tabStyles.stackedTab].join(' ')}>
                    {/* Region 2 & Region 3: Historical Comparison & Anthropometric Deltas */}
                    {prevMeasurement ? (
                        <Row className={tabStyles.responsiveRow}>
                            {/* Region 2: Previous Measurement */}
                            <Col xs={24} md={12} className={tabStyles.column}><Card className={[tabStyles.tabPanel, tabStyles.previousMeasurement].join(' ')} >
                                <div className={tabStyles.detailText}>
                                    VÙNG 2: SỐ LIỆU ĐO LẦN TRƯỚC
                                </div>
                                <div className={tabStyles.detailText2}>
                                    Ngày đo: {new Date(prevMeasurement.recordedAtUtc).toLocaleDateString('vi-VN')} ({new Date(prevMeasurement.recordedAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })})
                                </div>
                                <Row className={[tabStyles.responsiveRow, tabStyles.sectionSpacing2].join(' ')}>
                                    <Col xs={24} md={8} className={tabStyles.column}><div className={tabStyles.measurementTile}>
                                        <div className={tabStyles.detailText3}>Cân nặng</div>
                                        <div className={tabStyles.detailText4}>
                                            {prevMeasurement.weight ? `${prevMeasurement.weight} kg` : '--'}
                                        </div>
                                    </div></Col>
                                    <Col xs={24} md={8} className={tabStyles.column}><div className={tabStyles.measurementTile}>
                                        <div className={tabStyles.detailText3}>Chiều cao</div>
                                        <div className={tabStyles.detailText4}>
                                            {prevMeasurement.height ? `${prevMeasurement.height} cm` : '--'}
                                        </div>
                                    </div></Col>
                                    <Col xs={24} md={8} className={tabStyles.column}><div className={tabStyles.measurementTile}>
                                        <div className={tabStyles.detailText3}>BMI cũ</div>
                                        <div className={tabStyles.detailText4}>
                                            {prevMeasurement.bmi ? prevMeasurement.bmi : '--'}
                                        </div>
                                    </div></Col>
                                </Row>
                            </Card></Col>

                            {/* Region 3: Anthropometric Deltas (Color & Badge) */}
                            <Col xs={24} md={12} className={tabStyles.column}><Card className={[tabStyles.tabPanel, tabStyles.detail6].join(' ')} >
                                <div className={tabStyles.detailText5}>
                                    VÙNG 3: BIẾN ĐỘNG THỂ TRẠNG (DELTAS)
                                </div>
                                <div className={tabStyles.detailText6}>
                                    Chênh lệch so với lần khám trước
                                </div>
                                <Row className={[tabStyles.responsiveRow, tabStyles.sectionSpacing2].join(' ')}>
                                    <Col xs={24} md={8} className={tabStyles.column}><div className={tabStyles.outlinedPanel2}>
                                        <div className={tabStyles.detailText3}>Δ Cân nặng</div>
                                        <div className={[tabStyles.measurementValue, ((comparison?.weightDeltaKg || 0) > 0 ? tabStyles.statusText : ((comparison?.weightDeltaKg || 0) < 0 ? tabStyles.statusText2 : tabStyles.statusText3))].join(' ')}>
                                            {comparison?.weightDeltaKg !== undefined && comparison?.weightDeltaKg !== null
                                                ? (comparison.weightDeltaKg > 0 ? `+${comparison.weightDeltaKg} kg` : `${comparison.weightDeltaKg} kg`)
                                                : '--'}
                                        </div>
                                    </div></Col>
                                    <Col xs={24} md={8} className={tabStyles.column}><div className={tabStyles.outlinedPanel2}>
                                        <div className={tabStyles.detailText3}>Δ Chiều cao</div>
                                        <div className={tabStyles.detailText7}>
                                            {comparison?.heightDeltaCm !== undefined && comparison?.heightDeltaCm !== null
                                                ? (comparison.heightDeltaCm > 0 ? `+${comparison.heightDeltaCm} cm` : `${comparison.heightDeltaCm} cm`)
                                                : '--'}
                                        </div>
                                    </div></Col>
                                    <Col xs={24} md={8} className={tabStyles.column}><div className={tabStyles.outlinedPanel2}>
                                        <div className={tabStyles.detailText3}>Δ BMI</div>
                                        <div className={[tabStyles.measurementValue, ((comparison?.bmiDelta || 0) > 0 ? tabStyles.statusText : ((comparison?.bmiDelta || 0) < 0 ? tabStyles.statusText2 : tabStyles.statusText3))].join(' ')}>
                                            {comparison?.bmiDelta !== undefined && comparison?.bmiDelta !== null
                                                ? (comparison.bmiDelta > 0 ? `+${comparison.bmiDelta}` : `${comparison.bmiDelta}`)
                                                : '--'}
                                        </div>
                                    </div></Col>
                                </Row>
                            </Card></Col>
                        </Row>
                    ) : (
                        <Card className={[tabStyles.tabPanel, tabStyles.detailText8].join(' ')} >
                            ℹ️ Đây là lần đầu bệnh nhân ghi nhận dấu hiệu sinh tồn tại phòng khám. Dữ liệu so sánh thể trạng (deltas) sẽ xuất hiện từ lần khám kế tiếp.
                        </Card>
                    )}

                    {/* Region 1: Current Measurement Form */}
                    <Card className={[tabStyles.tabPanel].join(' ')} >
                        <div className={tabStyles.sectionHeader}>
                            <div>
                                <h3 className={tabStyles.sectionTitle}>
                                    VÙNG 1: ĐO LƯỜNG SINH HIỆU HIỆN TẠI
                                </h3>
                                <p className={tabStyles.description}>
                                    Nhập kết quả đo tại phòng khám hôm nay. BMI được tự động tính và phân loại.
                                </p>
                            </div>
                            <Button
                                htmlType="button"
                                className={[tabStyles.actionButton].join(' ')}
                                onClick={handleSaveVitals}
                                disabled={savingVitals}
                            >
                                <Save size={16} />
                                <span>{savingVitals ? 'Đang lưu...' : 'Lưu dấu hiệu sinh tồn'}</span>
                            </Button>
                        </div>

                        <Row className={[tabStyles.responsiveRow, tabStyles.sectionSpacing3].join(' ')}>
                            <Col xs={24} md={12} xl={8} className={tabStyles.column}>
                                <Form.Item className={tabStyles.formItem}>
                                    <label className={tabStyles.fieldLabel}>
                                        Nhiệt độ (°C)
                                    </label>
                                    <Input
                                        type="number"
                                        step="0.1"
                                        min="30"
                                        max="45"
                                        value={temperature}
                                        onChange={(e) => setTemperature(e.target.value)}
                                        placeholder="37.0"
                                        className={[tabStyles.fieldControl].join(' ')}
                                    />
                                </Form.Item>
                            </Col>

                            <Col xs={24} md={12} xl={8} className={tabStyles.column}>
                                <Form.Item className={tabStyles.formItem}>
                                    <label className={tabStyles.fieldLabel}>
                                        Huyết áp (Tâm thu / Tâm trương)
                                    </label>
                                    <div className={tabStyles.inlineGroup2}>
                                        <Input
                                            type="number"
                                            min="40"
                                            max="260"
                                            value={bpSystolic}
                                            onChange={(e) => setBpSystolic(e.target.value)}
                                            placeholder="120"
                                            className={[tabStyles.fieldControl].join(' ')}
                                        />
                                        <span className={tabStyles.detail8}>/</span>
                                        <Input
                                            type="number"
                                            min="30"
                                            max="180"
                                            value={bpDiastolic}
                                            onChange={(e) => setBpDiastolic(e.target.value)}
                                            placeholder="80"
                                            className={[tabStyles.fieldControl].join(' ')}
                                        />
                                        <span className={tabStyles.detailText9}>mmHg</span>
                                    </div>
                                </Form.Item>
                            </Col>

                            <Col xs={24} md={12} xl={8} className={tabStyles.column}>
                                <Form.Item className={tabStyles.formItem}>
                                    <label className={tabStyles.fieldLabel}>
                                        Nhịp tim / Mạch (nhịp/phút)
                                    </label>
                                    <Input
                                        type="number"
                                        min="30"
                                        max="220"
                                        value={heartRate}
                                        onChange={(e) => setHeartRate(e.target.value)}
                                        placeholder="75"
                                        className={[tabStyles.fieldControl].join(' ')}
                                    />
                                </Form.Item>
                            </Col>

                            <Col xs={24} md={12} xl={8} className={tabStyles.column}>
                                <Form.Item className={tabStyles.formItem}>
                                    <label className={tabStyles.fieldLabel}>
                                        Nhịp thở (lần/phút)
                                    </label>
                                    <Input
                                        type="number"
                                        min="8"
                                        max="60"
                                        value={respiratoryRate}
                                        onChange={(e) => setRespiratoryRate(e.target.value)}
                                        placeholder="18"
                                        className={[tabStyles.fieldControl].join(' ')}
                                    />
                                </Form.Item>
                            </Col>

                            <Col xs={24} md={12} xl={8} className={tabStyles.column}><div>
                                <div className={tabStyles.fieldLabelRow}>
                                    <label className={tabStyles.fieldLabel2}>
                                        Chiều cao (cm)
                                    </label>
                                    {previousHeight && !height && (
                                        <Button
                                            htmlType="button"
                                            onClick={() => setHeight(previousHeight.toString())}

                                            title="Tái sử dụng chiều cao từ lần đo trước" className={[tabStyles.actionButton, tabStyles.detailText10].join(' ')}
                                        >
                                            Dùng chiều cao lần trước ({previousHeight} cm)
                                        </Button>
                                    )}
                                </div>
                                <Input
                                    type="number"
                                    step="0.5"
                                    min="30"
                                    max="250"
                                    value={height}
                                    onChange={(e) => setHeight(e.target.value)}
                                    placeholder="170"
                                    className={[tabStyles.fieldControl].join(' ')}
                                />
                            </div></Col>

                            <Col xs={24} md={12} xl={8} className={tabStyles.column}>
                                <Form.Item className={tabStyles.formItem}>
                                    <label className={tabStyles.fieldLabel}>
                                        Cân nặng (kg)
                                    </label>
                                    <Input
                                        type="number"
                                        step="0.1"
                                        min="2"
                                        max="300"
                                        value={weight}
                                        onChange={(e) => setWeight(e.target.value)}
                                        placeholder="65.0"
                                        className={[tabStyles.fieldControl].join(' ')}
                                    />
                                </Form.Item>
                            </Col>

                            <Col xs={24} md={12} xl={8} className={tabStyles.column}>
                                <Form.Item className={tabStyles.formItem}>
                                    <label className={tabStyles.fieldLabel}>
                                        Nồng độ oxy SpO2 (%)
                                    </label>
                                    <Input
                                        type="number"
                                        min="50"
                                        max="100"
                                        value={spO2}
                                        onChange={(e) => setSpO2(e.target.value)}
                                        placeholder="98"
                                        className={[tabStyles.fieldControl].join(' ')}
                                    />
                                </Form.Item>
                            </Col>
                        </Row>

                        {/* Calculated BMI Badge Card */}
                        <div className={tabStyles.sectionHeader3}>
                            <div>
                                <div className={tabStyles.detailText11}>CHỈ SỐ KHỐI CƠ THỂ (BMI) TỰ ĐỘNG</div>
                                <div className={tabStyles.inlineGroup3}>
                                    <span className={tabStyles.detailText12}>
                                        {computedBmi !== null ? computedBmi : '--'}
                                    </span>
                                    {bmiClassification && (
                                        <span className={[tabStyles.bmiBadge, (bmiClassification.color === '#15803d' ? tabStyles.statusBackground : (bmiClassification.color === '#d97706' ? tabStyles.statusBackground2 : tabStyles.statusBackground3)), (bmiClassification.color === '#15803d' ? tabStyles.statusText3 : bmiClassification.color === '#d97706' ? tabStyles.statusText : tabStyles.statusText4)].join(' ')}>
                                            {bmiClassification.label}
                                        </span>
                                    )}
                                </div>
                            </div>

                            <div className={tabStyles.bmiExplanation}>
                                * BMI được tính theo công thức kg/m² và phân nhóm theo ngưỡng đang cấu hình trong hệ thống: Thiếu cân (&lt;18.5), Bình thường (18.5 - 24.9), Tiền béo phì (25 - 29.9), Béo phì (≥30).
                            </div>
                        </div>
                    </Card>

                    {/* Region 4: Vital Signs Longitudinal History Table */}
                    <Card className={[tabStyles.tabPanel].join(' ')} >
                        <div className={tabStyles.sectionHeader}>
                            <h3 className={tabStyles.regionTitle}>
                                Bảng theo dõi lịch sử sinh hiệu qua các lần khám ({historyList.length} lần đo)
                            </h3>
                        </div>

                        {historyList.length === 0 ? (
                            <EmptyState title="Chưa có dữ liệu lịch sử sinh hiệu từ các lần khám trước." />
                        ) : (
                            <div className={tabStyles.detail10}>
                                <div className={tabStyles.tableScroll}><DataTable data={historyList} keyExtractor={(item) => historyList.indexOf(item)} columns={[
                                    {
                                        header: "Thời điểm đo", accessor: (item) => {
                                            return <>
                                                {new Date(item.recordedAtUtc).toLocaleDateString('vi-VN')} {new Date(item.recordedAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                                            </>;
                                        }
                                    },
                                    {
                                        header: "Huyết áp (mmHg)", accessor: (item) => {
                                            return <>
                                                {item.bloodPressureSystolic && item.bloodPressureDiastolic ? `${item.bloodPressureSystolic}/${item.bloodPressureDiastolic}` : '--'}
                                            </>;
                                        }
                                    },
                                    { header: "Mạch (nhịp/phút)", accessor: (item) => { return <>{item.heartRate || '--'}</>; } },
                                    { header: "Nhiệt độ (°C)", accessor: (item) => { return <>{item.temperature ? `${item.temperature}°C` : '--'}</>; } },
                                    { header: "SpO2 (%)", accessor: (item) => { return <>{item.spO2 ? `${item.spO2}%` : '--'}</>; } },
                                    { header: "Cân nặng (kg)", accessor: (item) => { return <>{item.weight || '--'}</>; } },
                                    { header: "Chiều cao (cm)", accessor: (item) => { return <>{item.height || '--'}</>; } },
                                    { header: "BMI", accessor: (item) => { return <>{item.bmi || '--'}</>; } },
                                    { header: "Người đo", accessor: (item) => { return <>{item.recordedByUserName || '--'}</>; } }
                                ]} /></div>
                            </div>
                        )}
                    </Card>
                </Card>
            )}

            {/* Tab 3: Diagnostic Orders (Chỉ định Cận lâm sàng) */}
            {activeTab === 'diagnostics' && (
                <Card className={[tabStyles.tabPanel, tabStyles.stackedTab].join(' ')}>
                    {pollError && (
                        <InlineError
                            title="Tự động cập nhật bị gián đoạn"
                            message="Tự động cập nhật kết quả cận lâm sàng bị gián đoạn. Kết quả hiển thị có thể chưa mới nhất."
                            onRetry={() => loadDiagnosticOrders()}
                        />
                    )}
                    {/* Diagnostic Create Form */}
                    {!isCompleted && (
                        <Card className={[tabStyles.tabPanel].join(' ')} >
                            <div className={tabStyles.sectionHeader}>
                                <div>
                                    <h3 className={tabStyles.sectionTitle}>
                                        Tạo phiếu chỉ định Cận lâm sàng mới
                                    </h3>
                                    <p className={tabStyles.description}>
                                        Chọn các xét nghiệm hoặc chẩn đoán hình ảnh từ danh mục để chuyển đến Kỹ thuật viên.
                                    </p>
                                </div>
                            </div>

                            {/* Category filter tabs */}
                            <div className={tabStyles.inlineGroup5}>
                                {['All', 'Laboratory', 'Ultrasound', 'Imaging', 'Other'].map(cat => (
                                    <Button
                                        key={cat}
                                        htmlType="button"
                                        onClick={() => setCatalogCategory(cat)}
                                        className={[tabStyles.actionButton, tabStyles.detail11, (catalogCategory === cat ? tabStyles.statusBackground4 : tabStyles.statusBackground5), (catalogCategory === cat ? tabStyles.statusBackground6 : tabStyles.statusBackground7), (catalogCategory === cat ? tabStyles.statusText2 : tabStyles.statusText5)].join(' ')}
                                    >
                                        {cat === 'All' ? 'Tất cả danh mục' : categoryMap[cat] || cat}
                                    </Button>
                                ))}
                            </div>

                            {/* Services Catalog Selection Grid */}
                            <Row className={[tabStyles.responsiveRow, tabStyles.outlinedPanel3].join(' ')}>
                                {filteredCatalog.map(srv => {
                                    const isSelected = selectedServiceIds.includes(srv.id);
                                    return (
                                        <Col key={srv.id} xs={24} md={12} xl={8} className={tabStyles.column}><div
                                            key={srv.id}
                                            onClick={() => handleToggleSelectService(srv.id)}
                                            className={[tabStyles.serviceOption, (isSelected ? tabStyles.statusBackground4 : tabStyles.statusBackground5), (isSelected ? tabStyles.statusBackground8 : tabStyles.statusBackground7)].join(' ')}
                                        >
                                            <div className={tabStyles.inlineGroup2}>
                                                <input
                                                    type="checkbox"
                                                    checked={isSelected}
                                                    onChange={() => { }}
                                                    className={tabStyles.serviceCheckbox}
                                                />
                                                <div>
                                                    <div className={tabStyles.serviceName}>
                                                        {srv.name}
                                                    </div>
                                                    <div className={tabStyles.detailText3}>
                                                        <code>{srv.code}</code> • {categoryMap[srv.category] || srv.category}
                                                    </div>
                                                </div>
                                            </div>
                                            {srv.preparationInstructions && (
                                                <div className={tabStyles.preparationInstructions}>
                                                    {srv.preparationInstructions}
                                                </div>
                                            )}
                                        </div></Col>
                                    );
                                })}
                            </Row>

                            {/* Indication and Notes */}
                            <Row className={[tabStyles.responsiveRow, tabStyles.sectionSpacing].join(' ')}>
                                <Col xs={24} md={12} className={tabStyles.column}>
                                    <Form.Item className={tabStyles.formItem}>
                                        <label className={tabStyles.fieldLabel}>
                                            Chỉ định lâm sàng / Mục đích cận lâm sàng
                                        </label>
                                        <Input
                                            type="text"
                                            value={clinicalIndication}
                                            onChange={(e) => setClinicalIndication(e.target.value)}
                                            placeholder="Ví dụ: Kiểm tra men gan / Nghi ngờ sỏi thận..."
                                            className={[tabStyles.fieldControl].join(' ')}
                                        />
                                    </Form.Item>
                                </Col>
                                <Col xs={24} md={12} className={tabStyles.column}>
                                    <Form.Item className={tabStyles.formItem}>
                                        <label className={tabStyles.fieldLabel}>
                                            Ghi chú / Lưu ý cho Kỹ thuật viên
                                        </label>
                                        <Input
                                            type="text"
                                            value={orderNotes}
                                            onChange={(e) => setOrderNotes(e.target.value)}
                                            placeholder="Ví dụ: Bệnh nhân nhịn ăn sáng / Lấy máu cẩn thận..."
                                            className={[tabStyles.fieldControl].join(' ')}
                                        />
                                    </Form.Item>
                                </Col>
                            </Row>

                            <div className={tabStyles.fieldLabelRow}>
                                <div className={tabStyles.detailText17}>
                                    Đã chọn: <strong>{selectedServiceIds.length}</strong> dịch vụ
                                </div>
                                <Button type="primary"
                                    htmlType="button"
                                    onClick={handleCreateDiagnosticOrder}
                                    disabled={creatingOrder || selectedServiceIds.length === 0}
                                    className={[tabStyles.actionButton, tabStyles.refreshAction].join(' ')}
                                >
                                    <FlaskConical size={16} />
                                    <span>{creatingOrder ? 'Đang tạo...' : 'Tạo phiếu chỉ định'}</span>
                                </Button>
                            </div>
                        </Card>
                    )}

                    {/* Diagnostic Orders List */}
                    <Card className={[tabStyles.tabPanel].join(' ')} >
                        <div className={tabStyles.sectionHeader}>
                            <h3 className={tabStyles.sectionTitle}>
                                Danh sách phiếu chỉ định cận lâm sàng ({diagnosticOrders.length})
                            </h3>
                            <Button
                                htmlType="button"
                                onClick={() => void loadDiagnosticOrders()}
                                className={[tabStyles.actionButton, tabStyles.inlineGroup6].join(' ')}
                            >
                                <RefreshCw size={14} className={loadingOrders ? 'animate-spin' : ''} />
                                <span>Cập nhật kết quả</span>
                            </Button>
                        </div>

                        {diagnosticOrders.length === 0 ? (
                            <EmptyState icon={<FlaskConical size={36} className={tabStyles.detail14} />} title="Chưa có phiếu chỉ định cận lâm sàng nào trong ca khám này." description="Sử dụng danh mục phía trên để lập phiếu chỉ định gửi sang Kỹ thuật viên." />
                        ) : (
                            <div className={tabStyles.inlineGroup}>
                                {diagnosticOrders.map(order => (
                                    <div
                                        key={order.id}
                                        className={tabStyles.orderCard}
                                    >
                                        {/* Order Header */}
                                        <div className={tabStyles.orderHeader}>
                                            <div className={tabStyles.inlineGroup7}>
                                                <span className={tabStyles.orderCode}>
                                                    {order.orderCode}
                                                </span>
                                                {statusBadge(order.status)}
                                                {order.reviewedAtUtc ? (
                                                    <span className={tabStyles.reviewedBadge}>
                                                        <Check size={12} />
                                                        <span>Bác sĩ đã xem: {new Date(order.reviewedAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}</span>
                                                    </span>
                                                ) : (
                                                    order.status === 'Completed' && (
                                                        <span className={tabStyles.unreviewedBadge}>
                                                            Chưa duyệt kết quả
                                                        </span>
                                                    )
                                                )}
                                                <span className={tabStyles.detailText21}>
                                                    Thời gian lập: {new Date(order.orderedAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                                                </span>
                                            </div>

                                            <div className={tabStyles.inlineGroup2}>
                                                {/* Print Slip Button */}
                                                <Button
                                                    href={`/doctor/diagnostic-orders/${order.id}/print`}
                                                    target="_blank"
                                                    rel="noopener noreferrer"
                                                    className={[tabStyles.actionButton, tabStyles.inlineGroup8].join(' ')}
                                                >
                                                    <Printer size={14} />
                                                    <span>In phiếu chỉ định</span>
                                                </Button>

                                                {/* Doctor Review Confirmation Button */}
                                                {order.status === 'Completed' && !order.reviewedAtUtc && (
                                                    <Button type="primary"
                                                        htmlType="button"
                                                        onClick={() => handleReviewOrder(order.id)}
                                                        disabled={actionOrderId === order.id}
                                                        className={[tabStyles.actionButton, tabStyles.inlineGroup9].join(' ')}
                                                    >
                                                        <Check size={14} />
                                                        <span>{actionOrderId === order.id ? 'Đang xử lý...' : 'Xác nhận đã xem kết quả'}</span>
                                                    </Button>
                                                )}

                                                {/* Cancel Button */}
                                                {order.status === 'Ordered' && !isCompleted && (
                                                    <Button
                                                        htmlType="button"
                                                        onClick={() => handleCancelOrder(order.id)}
                                                        disabled={actionOrderId === order.id}
                                                        className={[tabStyles.actionButton, tabStyles.detailText22].join(' ')}
                                                    >
                                                        Hủy
                                                    </Button>
                                                )}
                                            </div>
                                        </div>

                                        {/* Order Items & Results Table */}
                                        <div className={tabStyles.detail15}>
                                            {order.clinicalIndication && (
                                                <div className={tabStyles.detailText23}>
                                                    <strong>Chỉ định lâm sàng:</strong> {order.clinicalIndication}
                                                </div>
                                            )}

                                            <div className={tabStyles.tableScroll}><DataTable data={order.items} keyExtractor={(item) => item.id} columns={[
                                                {
                                                    header: "Dịch vụ chỉ định", accessor: (item) => {
                                                        return <>
                                                            <div className={tabStyles.medicineName}>{item.serviceName}</div>
                                                            <div className={tabStyles.detailText3}>{item.serviceCode}</div>
                                                        </>;
                                                    }
                                                },
                                                {
                                                    header: "Phân loại", accessor: (item) => {
                                                        return <>
                                                            {categoryMap[item.category] || item.category}
                                                        </>;
                                                    }
                                                },
                                                {
                                                    header: "Trạng thái", accessor: (item) => {
                                                        return <>
                                                            {statusBadge(item.status)}
                                                        </>;
                                                    }
                                                },
                                                {
                                                    header: "Kết quả đo / Trị số", accessor: (item) => {
                                                        return <>
                                                            {item.result ? (
                                                                <div className={tabStyles.detail17}>
                                                                    {item.result.resultText || '--'} {item.result.unit}
                                                                </div>
                                                            ) : (
                                                                <span className={tabStyles.detail18}>Chưa có</span>
                                                            )}
                                                        </>;
                                                    }
                                                },
                                                {
                                                    header: "Chỉ số tham chiếu", accessor: (item) => {
                                                        return <>
                                                            {item.result?.referenceRange || '--'}
                                                        </>;
                                                    }
                                                },
                                                {
                                                    header: "Kết luận / Nhận xét", accessor: (item) => {
                                                        return <>
                                                            {item.result ? (
                                                                <div>
                                                                    {item.result.conclusion && (
                                                                        <div className={tabStyles.medicineName}>{item.result.conclusion}</div>
                                                                    )}
                                                                    <div className={tabStyles.detailText24}>
                                                                        KTV: {item.result.resultedByUserName} • {new Date(item.result.resultedAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                                                                    </div>
                                                                </div>
                                                            ) : (
                                                                <span className={tabStyles.detail18}>--</span>
                                                            )}
                                                        </>;
                                                    }
                                                }
                                            ]} /></div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </Card>
                </Card>
            )}

            {/* Tab 4: Prescription */}
            {activeTab === 'prescription' && (
                <Card className={[tabStyles.tabPanel].join(' ')} >
                    <Row className={tabStyles.responsiveRow}>
                        <Col span={24} className={tabStyles.column}>
                            <div className={tabStyles.prescriptionHeader}>
                                <div>
                                    <h3 className={tabStyles.sectionTitle}>
                                        Kê đơn thuốc cho bệnh nhân
                                    </h3>
                                    <p className={tabStyles.description}>
                                        Tìm kiếm thuốc từ danh mục hoạt động của phòng khám và điều chỉnh liều dùng.
                                    </p>
                                </div>
                                <Button
                                    htmlType="button"
                                    className={[tabStyles.actionButton].join(' ')}
                                    onClick={handleSavePrescriptionDraft}
                                    disabled={savingPrescription}
                                >
                                    <Save size={16} />
                                    <span>{savingPrescription ? 'Đang lưu...' : 'Lưu nháp đơn thuốc'}</span>
                                </Button>
                            </div>

                            {/* Medicine Search Box */}
                            <div className={tabStyles.medicineSearchSection}>
                                <div className={tabStyles.medicineSearch}>
                                    <Input
                                        prefix={<Search size={18} />}
                                        type="text"
                                        placeholder="Gõ tên thuốc hoặc mã thuốc để tìm kiếm và thêm vào đơn..."
                                        value={medicineSearch}
                                        onChange={(e) => setMedicineSearch(e.target.value)}
                                        className={[tabStyles.fieldControl].join(' ')}
                                    />
                                </div>

                                {/* Search Dropdown Results */}
                                {medicineSearch.trim() && (
                                    <div className={tabStyles.medicineResults}>
                                        {filteredMedicines.length === 0 ? (
                                            <EmptyState title={`Không tìm thấy thuốc khớp với "${medicineSearch}" trong kho.`} />
                                        ) : (
                                            filteredMedicines.map(med => (
                                                <div
                                                    key={med.id}
                                                    onClick={() => handleAddMedicine(med)} className={tabStyles.medicineOption}
                                                >
                                                    <div>
                                                        <span className={tabStyles.medicineName}>{med.name}</span>
                                                        <span className={tabStyles.medicineCode}>
                                                            ({med.code}) • ĐVT: {med.unit}
                                                        </span>
                                                    </div>
                                                    <div className={tabStyles.inlineGroup2}>
                                                        <span className={[tabStyles.detail21, (med.stockQuantity > 0 ? tabStyles.statusText3 : tabStyles.statusText4)].join(' ')}>
                                                            Tồn: {med.stockQuantity} {med.unit}
                                                        </span>
                                                        <span className={tabStyles.detailText26}>
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
                                <EmptyState icon={<Pill size={36} className={tabStyles.detail14} />} title="Chưa có thuốc nào trong đơn." description="Sử dụng ô tìm kiếm phía trên để thêm thuốc vào đơn." />
                            ) : (
                                <div className={tabStyles.sectionSpacing5}>
                                    <div className={tabStyles.tableScroll}><DataTable data={prescriptionItems} keyExtractor={(item) => item.medicineId} columns={[
                                        { header: "#", accessor: (item) => { const idx = prescriptionItems.indexOf(item); return <>{idx + 1}</>; } },
                                        {
                                            header: "Tên thuốc", accessor: (item) => {
                                                return <>
                                                    <div className={tabStyles.medicineName}>{item.medicineName}</div>
                                                    <div className={tabStyles.detailText3}>
                                                        {item.medicineCode} • {item.unit}
                                                    </div>
                                                </>;
                                            }
                                        },
                                        {
                                            header: "Số lượng", width: '90px', accessor: (item) => {
                                                const idx = prescriptionItems.indexOf(item); return <>
                                                    <Input
                                                        type="number"
                                                        min="1"
                                                        value={item.quantity}
                                                        onChange={(e) => handleItemChange(idx, 'quantity', parseInt(e.target.value, 10) || 1)}
                                                        className={[tabStyles.fieldControl].join(' ')}
                                                    />
                                                </>;
                                            }
                                        },
                                        {
                                            header: "Liều dùng", width: '110px', accessor: (item) => {
                                                const idx = prescriptionItems.indexOf(item); return <>
                                                    <Input
                                                        type="text"
                                                        value={item.dosage}
                                                        onChange={(e) => handleItemChange(idx, 'dosage', e.target.value)}
                                                        placeholder="1 viên"
                                                        className={[tabStyles.fieldControl].join(' ')}
                                                    />
                                                </>;
                                            }
                                        },
                                        {
                                            header: "Tần suất", width: '140px', accessor: (item) => {
                                                const idx = prescriptionItems.indexOf(item); return <>
                                                    <Input
                                                        type="text"
                                                        value={item.frequency}
                                                        onChange={(e) => handleItemChange(idx, 'frequency', e.target.value)}
                                                        placeholder="Ngày 2 lần"
                                                        className={[tabStyles.fieldControl].join(' ')}
                                                    />
                                                </>;
                                            }
                                        },
                                        {
                                            header: "Số ngày", width: '90px', accessor: (item) => {
                                                const idx = prescriptionItems.indexOf(item); return <>
                                                    <Input
                                                        type="number"
                                                        min="1"
                                                        max="90"
                                                        value={item.durationDays}
                                                        onChange={(e) => handleItemChange(idx, 'durationDays', parseInt(e.target.value, 10) || 1)}
                                                        className={[tabStyles.fieldControl].join(' ')}
                                                    />
                                                </>;
                                            }
                                        },
                                        {
                                            header: "Hướng dẫn uống", accessor: (item) => {
                                                const idx = prescriptionItems.indexOf(item); return <>
                                                    <Input
                                                        type="text"
                                                        value={item.instructions}
                                                        onChange={(e) => handleItemChange(idx, 'instructions', e.target.value)}
                                                        placeholder="Uống sau khi ăn 30 phút..."
                                                        className={[tabStyles.fieldControl].join(' ')}
                                                    />
                                                </>;
                                            }
                                        },
                                        {
                                            header: "", width: '50px', accessor: (item) => {
                                                const idx = prescriptionItems.indexOf(item); return <>
                                                    <Button
                                                        htmlType="button"
                                                        onClick={() => handleRemoveMedicine(idx)}

                                                        title="Xóa thuốc khỏi đơn" className={[tabStyles.actionButton, tabStyles.statusText4].join(' ')}
                                                    >
                                                        <Trash2 size={16} />
                                                    </Button>
                                                </>;
                                            }
                                        }
                                    ]} /></div>
                                </div>
                            )}

                            <Form.Item className={tabStyles.formItem}>
                                <label className={tabStyles.fieldLabel}>
                                    Ghi chú đơn thuốc cho dược sĩ / bệnh nhân
                                </label>
                                <Input.TextArea
                                    value={prescriptionNotes}
                                    onChange={(e) => setPrescriptionNotes(e.target.value)}
                                    rows={2}
                                    placeholder="Lưu ý dị ứng hoặc hướng dẫn bảo quản thuốc..."
                                    className={[tabStyles.fieldControl].join(' ')}
                                />
                            </Form.Item>
                        </Col>
                    </Row>
                </Card>
            )}

            {/* Tab 5: Past Visits History */}
            {activeTab === 'history' && (
                <Card className={[tabStyles.tabPanel].join(' ')} >
                    <Row className={tabStyles.responsiveRow}>
                        <Col span={24} className={tabStyles.column}>
                            <h3 className={tabStyles.historyTitle}>
                                Lịch sử các lần khám trước của bệnh nhân ({patient.pastVisits.length} lượt)
                            </h3>

                            {patient.pastVisits.length === 0 ? (
                                <EmptyState icon={<History size={36} className={tabStyles.detail14} />} title="Đây là lần đầu bệnh nhân đến khám tại hệ thống phòng khám." />
                            ) : (
                                <div className={tabStyles.historyList}>
                                    {patient.pastVisits.map(visit => (
                                        <div key={visit.appointmentId} className={tabStyles.historyVisit}>
                                            <div className={tabStyles.historyHeader}>
                                                <div>
                                                    <span className={tabStyles.detailText27}>
                                                        Ngày: {visit.date}
                                                    </span>
                                                    <span className={tabStyles.detailText28}>
                                                        Mã: #{visit.appointmentCode}
                                                    </span>
                                                </div>
                                                <div className={tabStyles.detailText29}>
                                                    BS: {visit.doctorName} • Khoa: {visit.specialtyName}
                                                </div>
                                            </div>

                                            {visit.diagnosis && (
                                                <div className={tabStyles.detailText30}>
                                                    <strong>Chẩn đoán:</strong> {visit.diagnosis}
                                                </div>
                                            )}

                                            {visit.summary && (
                                                <div className={tabStyles.detailText31}>
                                                    <strong>Kết luận:</strong> {visit.summary}
                                                </div>
                                            )}

                                            {visit.prescriptionItemNames && visit.prescriptionItemNames.length > 0 && (
                                                <div className={tabStyles.inlineGroup11}>
                                                    <span className={tabStyles.detailText32}>Thuốc đã kê:</span>
                                                    {visit.prescriptionItemNames.map((medName, mIdx) => (
                                                        <span key={mIdx} className={tabStyles.detailText33}>
                                                            {medName}
                                                        </span>
                                                    ))}
                                                </div>
                                            )}
                                        </div>
                                    ))}
                                </div>
                            )}
                        </Col>
                    </Row>
                </Card>
            )}

            {/* Bottom Sticky Action Bar */}
            <div className={tabStyles.actionBar}>
                <div className={[tabStyles.inlineGroup2, tabStyles.statusSummary].join(' ')}>
                    <span className={tabStyles.recordStatusLabel}>Trạng thái hồ sơ:</span>
                    <StatusBadge status={isCompleted ? 'Completed' : 'InConsultation'} label={isCompleted ? 'Đã hoàn tất' : 'Đang khám'} />
                    {diagnosticOrders.length > 0 && (
                        <span className={tabStyles.diagnosticsSummary}>
                            CLS ({diagnosticOrders.length} phiếu)
                        </span>
                    )}
                    {prescriptionItems.length > 0 && (
                        <span className={tabStyles.prescriptionSummary}>
                            Đơn thuốc ({prescriptionItems.length} loại)
                        </span>
                    )}
                </div>

                {isCompleted ? (
                    <div className={tabStyles.inlineGroup16}>
                        <CheckCircle size={18} />
                        <span>Hồ sơ ca khám đã chốt và lưu trữ chính thức</span>
                    </div>
                ) : (
                    <div className={tabStyles.actionBarButtons}>
                        {pendingDiagnosticOrder && (
                            <span className={tabStyles.inlineGroup18}>
                                <AlertTriangle size={14} /> Có chỉ định CLS chờ xử lý
                            </span>
                        )}
                        {!pendingDiagnosticOrder && unreviewedDiagnosticOrder && (
                            <span className={tabStyles.inlineGroup19}>
                                <AlertCircle size={14} /> Có kết quả CLS chưa duyệt
                            </span>
                        )}

                        <Button
                            htmlType="button"
                            onClick={() => setIsRevisitModalOpen(true)}
                            className={[tabStyles.actionButton, tabStyles.inlineGroup2].join(' ')}
                        >
                            <Calendar size={16} />
                            <span>Hẹn tái khám</span>
                        </Button>

                        <Button type="primary"
                            htmlType="button"
                            onClick={handleOpenCompleteModal}
                            className={[tabStyles.actionButton, tabStyles.completionButtonContent, ((pendingDiagnosticOrder || unreviewedDiagnosticOrder) ? tabStyles.statusBackground9 : tabStyles.statusBackground10)].join(' ')}
                        >
                            <CheckCircle size={18} />
                            <span>HOÀN TẤT KHÁM BỆNH</span>
                        </Button>
                    </div>
                )}
            </div>

            {/* Revisit Modal */}
            {isRevisitModalOpen && (
                <div className={tabStyles.modalOverlay}>
                    <div className={tabStyles.revisitDialog}>
                        <h3 className={tabStyles.sectionTitle4}>
                            Đề xuất tái khám cho bệnh nhân
                        </h3>
                        <form onSubmit={handleCreateRevisit}>
                            <Form.Item className={tabStyles.formItem}>
                                <label className={tabStyles.fieldLabel}>
                                    Ngày hẹn tái khám đề xuất *
                                </label>
                                <Input
                                    type="date"
                                    value={revisitDate}
                                    onChange={(e) => setRevisitDate(e.target.value)}
                                    min={minRevisitDate}
                                    required
                                    className={[tabStyles.fieldControl].join(' ')}
                                />
                            </Form.Item>
                            <Form.Item className={tabStyles.formItem}>
                                <label className={tabStyles.fieldLabel}>
                                    Ghi chú / Nhắc nhở tái khám
                                </label>
                                <Input.TextArea
                                    value={revisitNote}
                                    onChange={(e) => setRevisitNote(e.target.value)}
                                    placeholder="Tái khám đánh giá lại triệu chứng hoặc kết quả xét nghiệm..."
                                    rows={3}
                                    className={[tabStyles.fieldControl].join(' ')}
                                />
                            </Form.Item>
                            <div className={tabStyles.inlineGroup22}>
                                <Button htmlType="button" onClick={() => setIsRevisitModalOpen(false)} className={tabStyles.actionButton} >
                                    Hủy
                                </Button>
                                <Button type="primary" htmlType="submit" disabled={savingRevisit} className={tabStyles.actionButton} >
                                    {savingRevisit ? 'Đang tạo...' : 'Xác nhận đề xuất'}
                                </Button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Complete Consultation Confirmation Modal */}
            {isCompleteModalOpen && (
                <div className={tabStyles.modalOverlay}>
                    <div className={tabStyles.completionDialog}>
                        <div className={tabStyles.inlineGroup23}>
                            <div className={tabStyles.inlineGroup24}>
                                <CheckCircle size={24} />
                            </div>
                            <div>
                                <h3 className={tabStyles.sectionTitle5}>
                                    Xác nhận hoàn tất ca khám
                                </h3>
                                <p className={tabStyles.description}>
                                    Giao dịch nguyên tử: Chốt hồ sơ bệnh án và phát hành đơn thuốc.
                                </p>
                            </div>
                        </div>

                        <div className={tabStyles.outlinedPanel7}>
                            <div className={tabStyles.sectionSpacing8}>
                                <strong>Bệnh nhân:</strong> {patient.patientName} ({patient.patientPhone})
                            </div>
                            <div className={tabStyles.sectionSpacing8}>
                                <strong>Chẩn đoán:</strong> {diagnosis || '<Chưa nhập>'}
                            </div>
                            <div className={tabStyles.sectionSpacing8}>
                                <strong>Tóm tắt:</strong> {summary || '<Chưa nhập>'}
                            </div>
                            <div className={tabStyles.sectionSpacing8}>
                                <strong>Cận lâm sàng:</strong> {diagnosticOrders.length > 0 ? `${diagnosticOrders.length} phiếu chỉ định (Đã có kết quả & đã xem)` : 'Không có chỉ định CLS'}
                            </div>
                            <div>
                                <strong>Đơn thuốc:</strong> {prescriptionItems.length > 0 ? `${prescriptionItems.length} loại thuốc` : 'Không kê đơn'}
                            </div>
                        </div>

                        {prescriptionItems.length > 0 && (
                            <Form.Item className={tabStyles.formItem}>
                                <label className={tabStyles.fieldLabel3}>
                                    <input
                                        type="checkbox"
                                        checked={issuePrescriptionCheck}
                                        onChange={(e) => setIssuePrescriptionCheck(e.target.checked)}
                                        className={tabStyles.confirmationCheckbox}
                                    />
                                    <span>Chốt và phát hành đơn thuốc sang Dược sĩ (Trạng thái Issued)</span>
                                </label>
                            </Form.Item>
                        )}

                        <div className={tabStyles.inlineGroup22}>
                            <Button
                                htmlType="button"
                                onClick={() => setIsCompleteModalOpen(false)}
                                className={tabStyles.actionButton}
                            >
                                Quay lại chỉnh sửa
                            </Button>
                            <Button type="primary"
                                htmlType="button"
                                onClick={handleCompleteConsultation}
                                disabled={completing || !diagnosis.trim() || !summary.trim() || Boolean(pendingDiagnosticOrder) || Boolean(unreviewedDiagnosticOrder)}
                                className={tabStyles.actionButton}
                            >
                                {completing ? 'Đang hoàn tất...' : 'Xác nhận hoàn tất'}
                            </Button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
