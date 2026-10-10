import React, { useState, useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Button, Card, Col, Input, Row, Select } from 'antd';
import { PageHeader, StatusBadge } from '../../components/common';
import { spacing } from '../../theme/tokens';
import styles from './WalkInPatientRegistration.module.css';
import {
    UserCheck, UserPlus, Search, ShieldAlert,
    Plus, Trash2, CheckCircle2, ArrowRight, ArrowLeft,
    RefreshCw
} from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { mpiApi, type MpiPatientDto } from '../../api/mpiApi';
import { organizationApi, type FacilityDto, type DepartmentDto, type RoomDto } from '../../api/organizationApi';
import { patientVisitApi } from '../../api/patientVisitApi';
import type { CheckInTicketDto, ReceptionIntakeRequest, VisitPriority, ApiResponse } from '../../types';
import { CheckInTicketModal } from '../../components/CheckInTicketModal';
import { useDialog } from '../../contexts/DialogContext';

interface AllergyItem {
    allergen: string;
    severity?: string;
    reaction?: string;
}

interface Doctor {
    id: number;
    fullName: string;
    specialtyName?: string;
}

export const WalkInPatientRegistration: React.FC = () => {
    const navigate = useNavigate();
    const location = useLocation();
    const { showAlert } = useDialog();

    // Stepper: 1 = Patient Lookup/Entry, 2 = Visit Setup, 3 = Confirmation
    const [step, setStep] = useState<1 | 2 | 3>(1);

    // Facilities & Doctors
    const [facilities, setFacilities] = useState<FacilityDto[]>([]);
    const [departments, setDepartments] = useState<DepartmentDto[]>([]);
    const [rooms, setRooms] = useState<RoomDto[]>([]);
    const [doctors, setDoctors] = useState<Doctor[]>([]);

    // Step 1: Patient Selection Mode ('lookup' or 'new')
    const [patientMode, setPatientMode] = useState<'lookup' | 'new'>('lookup');

    // Lookup state
    const [searchQuery, setSearchQuery] = useState('');
    const [searching, setSearching] = useState(false);
    const [searchResults, setSearchResults] = useState<MpiPatientDto[]>([]);
    const [selectedPatient, setSelectedPatient] = useState<MpiPatientDto | null>(null);

    // New Patient Form state
    const [newFullName, setNewFullName] = useState('');
    const [newPhone, setNewPhone] = useState('');
    const [newDateOfBirth, setNewDateOfBirth] = useState('');
    const [newGender, setNewGender] = useState<number>(0); // 0 = Male, 1 = Female, 2 = Other
    const [newIdentityCard, setNewIdentityCard] = useState('');
    const [newAddress, setNewAddress] = useState('');

    // Emergency Contact
    const [contactName, setContactName] = useState('');
    const [contactRelationship, setContactRelationship] = useState('Người thân');
    const [contactPhone, setContactPhone] = useState('');
    const [isGuardian, setIsGuardian] = useState(false);

    // Allergies list
    const [allergies, setAllergies] = useState<AllergyItem[]>([]);

    // Step 2: Visit Preparation state
    const [facilityId, setFacilityId] = useState<number>(1);
    const [departmentId, setDepartmentId] = useState<number | undefined>(undefined);
    const [roomId, setRoomId] = useState<number | undefined>(undefined);
    const [doctorId, setDoctorId] = useState<number | undefined>(undefined);
    const [priority, setPriority] = useState<VisitPriority>('Normal');
    const [chiefComplaint, setChiefComplaint] = useState('');

    // Submission & Ticket Modal
    const [submitting, setSubmitting] = useState(false);
    const [currentTicket, setCurrentTicket] = useState<CheckInTicketDto | null>(null);
    const [ticketModalOpen, setTicketModalOpen] = useState(false);

    // 1. Initial Load: Facilities
    useEffect(() => {
        organizationApi.getFacilities(false)
            .then(res => {
                if (res.success && res.data && res.data.length > 0) {
                    setFacilities(res.data);
                    setFacilityId(res.data[0].id);
                }
            })
            .catch(err => console.error('Lỗi tải cơ sở:', err));
    }, []);

    useEffect(() => {
        const controller = new AbortController();
        let active = true;
        axiosClient.get<any, ApiResponse<Doctor[]>>('/doctors', { params: { facilityId }, signal: controller.signal })
            .then(res => {
                if (!active) return;
                const available = res.success && res.data ? res.data : [];
                setDoctors(available);
                setDoctorId(current => available.some(doctor => doctor.id === current) ? current : undefined);
            })
            .catch(err => {
                if (!active) return;
                setDoctors([]);
                setDoctorId(undefined);
                console.error('Lỗi tải bác sĩ:', err);
            });
        return () => { active = false; controller.abort(); };
    }, [facilityId]);

    // 2. Check query params for pre-selected patient id
    useEffect(() => {
        const queryParams = new URLSearchParams(location.search);
        const existingPatientId = queryParams.get('existingPatientId');
        if (existingPatientId) {
            const pid = Number(existingPatientId);
            if (!isNaN(pid) && pid > 0) {
                mpiApi.getPatientById(pid).then(res => {
                    if (res.success && res.data) {
                        setSelectedPatient(res.data);
                        setPatientMode('lookup');
                    }
                }).catch(err => console.error('Lỗi nạp bệnh nhân:', err));
            }
        }
    }, [location.search]);

    // 3. Cascading Department -> Rooms
    useEffect(() => {
        if (facilityId) {
            organizationApi.getDepartments(facilityId)
                .then(res => {
                    if (res.success && res.data) {
                        setDepartments(res.data);
                        if (res.data.length > 0) {
                            setDepartmentId(res.data[0].id);
                        } else {
                            setDepartmentId(undefined);
                        }
                    }
                })
                .catch(err => console.error('Lỗi tải khoa:', err));
        }
    }, [facilityId]);

    useEffect(() => {
        if (facilityId && departmentId) {
            organizationApi.getRooms({ facilityId, departmentId })
                .then(res => {
                    if (res.success && res.data) {
                        setRooms(res.data);
                        setRoomId(res.data.length > 0 ? res.data[0].id : undefined);
                    }
                })
                .catch(err => console.error('Lỗi tải phòng:', err));
        } else {
            setRooms([]);
            setRoomId(undefined);
        }
    }, [facilityId, departmentId]);

    // Handle MPI Search
    const handleSearchPatient = async (e?: React.FormEvent) => {
        if (e) e.preventDefault();
        if (!searchQuery.trim()) {
            showAlert('Thông báo', 'Vui lòng nhập từ khóa tìm kiếm (Mã BN, CCCD, SĐT hoặc Họ tên).', 'warning');
            return;
        }

        setSearching(true);
        try {
            const res = await mpiApi.searchPatients({ searchTerm: searchQuery.trim(), page: 1, pageSize: 5 });
            if (res.success && res.data) {
                setSearchResults(res.data.items);
                if (res.data.items.length === 0) {
                    showAlert('Kết quả tìm kiếm', 'Không tìm thấy người bệnh nào khớp thông tin. Bạn có thể chuyển sang "Đăng ký hồ sơ mới".', 'info');
                }
            } else {
                setSearchResults([]);
            }
        } catch (err) {
            console.error('Lỗi tìm kiếm MPI:', err);
            showAlert('Lỗi tra cứu', 'Không thể kết nối dịch vụ tra cứu hồ sơ người bệnh.', 'error');
        } finally {
            setSearching(false);
        }
    };

    // Allergies helpers
    const handleAddAllergy = () => {
        setAllergies([...allergies, { allergen: '', severity: 'Moderate', reaction: '' }]);
    };

    const handleRemoveAllergy = (index: number) => {
        setAllergies(allergies.filter((_, i) => i !== index));
    };

    const handleUpdateAllergy = (index: number, field: keyof AllergyItem, value: string) => {
        const updated = [...allergies];
        updated[index] = { ...updated[index], [field]: value };
        setAllergies(updated);
    };

    // Stepper navigation validation
    const handleProceedToStep2 = () => {
        if (patientMode === 'lookup') {
            if (!selectedPatient) {
                showAlert('Chưa chọn người bệnh', 'Vui lòng chọn một hồ sơ người bệnh từ kết quả tra cứu trước khi tiếp tục.', 'warning');
                return;
            }
        } else {
            // Validate new patient
            if (!newFullName.trim()) {
                showAlert('Thiếu họ tên', 'Vui lòng nhập Họ và tên người bệnh.', 'warning');
                return;
            }
            if (!newDateOfBirth) {
                showAlert('Thiếu ngày sinh', 'Vui lòng chọn Ngày tháng năm sinh của người bệnh.', 'warning');
                return;
            }
            // Check contact phone rule: if personal phone is empty, emergency contact phone is required!
            if (!newPhone.trim() && !contactPhone.trim()) {
                showAlert(
                    'Yêu cầu số điện thoại liên hệ',
                    'Vui lòng nhập ít nhất một số điện thoại: Số điện thoại cá nhân của người bệnh HOẶC số điện thoại người liên hệ khẩn cấp.',
                    'warning'
                );
                return;
            }
        }
        setStep(2);
    };

    const handleProceedToStep3 = () => {
        if (!facilityId) {
            showAlert('Chưa chọn cơ sở', 'Vui lòng chọn cơ sở y tế tiếp nhận.', 'warning');
            return;
        }
        if (!departmentId) {
            showAlert('Chưa chọn khoa', 'Vui lòng chọn Khoa khám tiếp nhận.', 'warning');
            return;
        }
        if (!chiefComplaint.trim()) {
            showAlert('Thiếu lý do khám', 'Vui lòng nhập Lý do khám hoặc Triệu chứng ban đầu của người bệnh.', 'warning');
            return;
        }
        setStep(3);
    };

    // Step 3: Final Submission
    const handleConfirmIntake = async () => {
        if (!departmentId) return;

        setSubmitting(true);
        try {
            const idempotencyKey = crypto.randomUUID();

            const intakePayload: ReceptionIntakeRequest = {
                facilityId,
                departmentId,
                roomId: roomId || undefined,
                assignedDoctorId: doctorId || undefined,
                priority,
                chiefComplaint: chiefComplaint.trim(),
                idempotencyKey
            };

            if (patientMode === 'lookup' && selectedPatient) {
                intakePayload.existingPatientId = selectedPatient.id;
            } else {
                intakePayload.newPatient = {
                    fullName: newFullName.trim(),
                    phoneNumber: newPhone.trim() || undefined,
                    dateOfBirth: newDateOfBirth,
                    gender: newGender,
                    nationalId: newIdentityCard.trim() || undefined,
                    address: newAddress.trim() || undefined,
                    allergies: allergies.filter(a => a.allergen.trim()).map(a => ({
                        allergen: a.allergen.trim(),
                        severity: a.severity || 'Moderate',
                        reaction: a.reaction?.trim() || undefined
                    })),
                    emergencyContact: contactPhone.trim() ? {
                        contactName: contactName.trim() || 'Người liên hệ',
                        relationship: contactRelationship || 'Người thân',
                        phoneNumber: contactPhone.trim(),
                        isGuardian
                    } : undefined
                };
            }

            const res = await patientVisitApi.receptionIntake(intakePayload, idempotencyKey);
            if (res.success && res.data) {
                setCurrentTicket(res.data);
                setTicketModalOpen(true);
                showAlert(
                    'Tiếp nhận & Cấp STT thành công!',
                    `Đã cấp Số thứ tự ${res.data.queueNumber} tại ${res.data.departmentName || 'phòng khám'} cho bệnh nhân ${res.data.patientName}. Mã bệnh án: ${res.data.medicalRecordNumber}.`,
                    'success'
                );
            } else {
                showAlert('Lỗi tiếp nhận', res.message || 'Không thể tạo lượt khám.', 'error');
            }
        } catch (err: any) {
            console.error('Lỗi khi tiếp nhận bệnh nhân:', err);
            showAlert('Lỗi tiếp nhận', err.response?.data?.message || 'Không thể hoàn tất tiếp nhận lượt khám.', 'error');
        } finally {
            setSubmitting(false);
        }
    };

    const handleResetAll = () => {
        setStep(1);
        setPatientMode('lookup');
        setSelectedPatient(null);
        setSearchQuery('');
        setSearchResults([]);
        setNewFullName('');
        setNewPhone('');
        setNewDateOfBirth('');
        setNewGender(0);
        setNewIdentityCard('');
        setNewAddress('');
        setContactName('');
        setContactRelationship('Người thân');
        setContactPhone('');
        setIsGuardian(false);
        setAllergies([]);
        setChiefComplaint('');
        setPriority('Normal');
        setCurrentTicket(null);
    };

    return (
        <div className={styles.registration}>
            <PageHeader title="Quy Trình Tiếp Nhận Người Bệnh Khám" subtitle="Quy trình 3 bước chuẩn: Tra cứu hồ sơ cũ ➔ Chuẩn bị lượt khám ➔ Cấp số thứ tự & In phiếu"
                actions={<Button htmlType="button" onClick={() => navigate('/reception')}><ArrowLeft size={16} /> Bàn làm việc lễ tân</Button>} />
            <div className={styles.intakeSteps}>
                <div className={`${styles.intakeStep} ${step === 1 ? styles.activeStep : ''}`}><span className={`${styles.stepNumber} ${step >= 1 ? styles.reachedStepNumber : ''}`}>1</span><span>Tìm & Chọn Người Bệnh</span></div>
                <div className={`${styles.intakeStep} ${step === 2 ? styles.activeStep : ''}`}><span className={`${styles.stepNumber} ${step >= 2 ? styles.reachedStepNumber : ''}`}>2</span><span>Chuẩn Bị Lượt Khám</span></div>
                <div className={`${styles.intakeStep} ${step === 3 ? styles.activeStep : ''}`}><span className={`${styles.stepNumber} ${step >= 3 ? styles.reachedStepNumber : ''}`}>3</span><span>Xác Nhận & Cấp STT</span></div>
            </div>
            {step === 1 && <Card className={styles.stepCard}>
                <div className={styles.patientModeButtons}>
                    <Button htmlType="button" type={patientMode === 'lookup' ? 'primary' : 'default'} onClick={() => setPatientMode('lookup')}><Search size={16} /> Tra cứu hồ sơ cũ (Khuyên dùng)</Button>
                    <Button htmlType="button" type={patientMode === 'new' ? 'primary' : 'default'} onClick={() => setPatientMode('new')}><UserPlus size={16} /> Đăng ký hồ sơ người bệnh mới</Button>
                </div>
                {patientMode === 'lookup' && <div>
                    <form onSubmit={handleSearchPatient} className={styles.patientLookupForm}>
                        <Input type="text" className={styles.patientLookupInput} placeholder="Nhập Mã bệnh nhân (MRN), Số CCCD, Số điện thoại hoặc Họ tên..." value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} />
                        <Button htmlType="submit" type="primary" disabled={searching}>{searching ? <RefreshCw className="spin" size={16} /> : <Search size={16} />} Tìm hồ sơ</Button>
                    </form>
                    {selectedPatient && <div className={styles.selectedPatient}>
                        <div className={styles.selectedPatientHeader}>
                            <div className={styles.selectedPatientIdentity}><CheckCircle2 size={24} className={styles.selectionIcon} /><div><h3 className={styles.selectedPatientName}>{selectedPatient.fullName}</h3><div className={styles.selectedPatientIdentifiers}>Mã bệnh nhân (MRN): <strong>{selectedPatient.medicalRecordNumber || 'Chưa gán'}</strong> • CCCD: {selectedPatient.nationalId || '---'}</div></div></div>
                            <Button htmlType="button" onClick={() => setSelectedPatient(null)}>Chọn hồ sơ khác</Button>
                        </div>
                        <Row gutter={[spacing.md, spacing.sm]} className={styles.selectedPatientFacts}>
                            <Col xs={24} md={12}><strong>Ngày sinh:</strong> {selectedPatient.dateOfBirth || '---'}</Col>
                            <Col xs={24} md={12}><strong>Giới tính:</strong> {selectedPatient.genderName || selectedPatient.gender || '---'}</Col>
                            <Col xs={24} md={12}><strong>Số điện thoại:</strong> {selectedPatient.phoneNumber || '---'}</Col>
                            <Col xs={24} md={12}><strong>Địa chỉ:</strong> {selectedPatient.address || '---'}</Col>
                        </Row>
                    </div>}
                    {!selectedPatient && searchResults.length > 0 && <div className={styles.patientLookupResults}>
                        <div className={styles.lookupResultCount}>Tìm thấy {searchResults.length} hồ sơ người bệnh phù hợp:</div>
                        {searchResults.map(p => <div key={p.id} onClick={() => setSelectedPatient(p)} className={styles.patientLookupResult}>
                            <div className={styles.lookupPatientDetails}><div className={styles.lookupPatientName}>{p.fullName} <span className={styles.lookupMedicalRecord}>({p.medicalRecordNumber || 'Chưa có MRN'})</span></div><div className={styles.lookupPatientIdentifiers}>CCCD: {p.nationalId || '---'} • SĐT: {p.phoneNumber || '---'} • Sinh: {p.dateOfBirth || '---'} ({p.genderName || p.gender})</div></div>
                            <Button htmlType="button" type="primary"><UserCheck size={14} /> Chọn hồ sơ này</Button>
                        </div>)}
                    </div>}
                </div>}
                {patientMode === 'new' && <div>
                    <Row gutter={[spacing.md, spacing.md]} className={styles.patientFormRow}>
                        <Col xs={24} md={12}><div className={styles.formField}><label className={styles.fieldLabel}>Họ và tên người bệnh *</label><Input type="text" placeholder="VD: NGUYỄN VĂN A" value={newFullName} onChange={(e) => setNewFullName(e.target.value)} required /></div></Col>
                        <Col xs={24} md={12}><div className={styles.formField}><label className={styles.fieldLabel}>Số CCCD / CMND</label><Input type="text" placeholder="12 chữ số CCCD..." value={newIdentityCard} onChange={(e) => setNewIdentityCard(e.target.value)} /></div></Col>
                    </Row>
                    <Row gutter={[spacing.md, spacing.md]} className={styles.patientFormRow}>
                        <Col xs={24} md={8}><div className={styles.formField}><label className={styles.fieldLabel}>Ngày sinh *</label><Input type="date" value={newDateOfBirth} onChange={(e) => setNewDateOfBirth(e.target.value)} required /></div></Col>
                        <Col xs={24} md={8}><div className={styles.formField}><label className={styles.fieldLabel}>Giới tính *</label><select className={styles.nativeSelect} value={newGender} onChange={(e) => setNewGender(Number(e.target.value))}><option value={0}>Nam</option><option value={1}>Nữ</option><option value={2}>Khác</option></select></div></Col>
                        <Col xs={24} md={8}><div className={styles.formField}><label className={styles.fieldLabel}>Số điện thoại cá nhân<span className={styles.fieldHint}>(Tùy chọn nếu có SĐT khẩn cấp)</span></label><Input type="tel" placeholder="09xx xxx xxx" value={newPhone} onChange={(e) => setNewPhone(e.target.value)} /></div></Col>
                    </Row>
                    <div className={styles.addressField}><label className={styles.fieldLabel}>Địa chỉ cư trú</label><Input type="text" placeholder="Số nhà, tên đường, phường/xã, quận/huyện, tỉnh/thành..." value={newAddress} onChange={(e) => setNewAddress(e.target.value)} /></div>
                    <div className={styles.emergencyContactSection}>
                        <h4 className={styles.sectionTitle}>Người liên hệ khẩn cấp / Giám hộ (Bắt buộc nếu người bệnh không có SĐT)</h4>
                        <Row gutter={[spacing.md, spacing.md]} className={styles.patientFormRow}>
                            <Col xs={24} md={8}><div className={styles.formField}><label className={styles.fieldLabel}>Họ tên người liên hệ</label><Input type="text" placeholder="VD: Trần Thị B" value={contactName} onChange={(e) => setContactName(e.target.value)} /></div></Col>
                            <Col xs={24} md={8}><div className={styles.formField}><label className={styles.fieldLabel}>Mối quan hệ</label><Select className={styles.formSelect} value={contactRelationship} onChange={(value) => setContactRelationship(value)} options={[{value:'Bố/Mẹ',label:'Bố/Mẹ'},{value:'Vợ/Chồng',label:'Vợ/Chồng'},{value:'Con cái',label:'Con cái'},{value:'Người thân',label:'Người thân khác'},{value:'Người giám hộ',label:'Người giám hộ'}]} /></div></Col>
                            <Col xs={24} md={8}><div className={styles.formField}><label className={styles.fieldLabel}>Số điện thoại liên hệ</label><Input type="tel" placeholder="09xx xxx xxx" value={contactPhone} onChange={(e) => setContactPhone(e.target.value)} /></div></Col>
                        </Row>
                        <div className={styles.formField}><label htmlFor="emergency-contact-is-guardian" className={styles.guardianLabel}><input id="emergency-contact-is-guardian" type="checkbox" checked={isGuardian} onChange={(e) => setIsGuardian(e.target.checked)} disabled={!contactPhone.trim()} />Người liên hệ này là người giám hộ của người bệnh</label>{!contactPhone.trim() && <span className={styles.fieldHint}>Nhập số điện thoại liên hệ để chọn.</span>}</div>
                    </div>
                    <div className={styles.allergySection}>
                        <div className={styles.allergySectionHeader}><h4 className={styles.allergyTitle}><ShieldAlert size={16} /> Tiền sử dị ứng thuốc & thực phẩm</h4><Button htmlType="button" onClick={handleAddAllergy}><Plus size={13} /> Thêm dị ứng</Button></div>
                        {allergies.map((al, idx) => <Row key={idx} gutter={[spacing.xs, spacing.xs]} align="middle" className={styles.allergyRow}>
                            <Col xs={24} md={8}><Input type="text" placeholder="Dị nguyên (VD: Penicillin, Paracetamol...)" value={al.allergen} onChange={(e) => handleUpdateAllergy(idx, 'allergen', e.target.value)} /></Col>
                            <Col xs={24} md={5}><Select className={styles.formSelect} value={al.severity} onChange={(value) => handleUpdateAllergy(idx, 'severity', value)} options={[{value:'Mild',label:'Nhẹ'},{value:'Moderate',label:'Vừa'},{value:'Severe',label:'Nặng / Sốc phản vệ'}]} /></Col>
                            <Col xs={24} md={8}><Input type="text" placeholder="Phản ứng (VD: Nổi mẩn, khó thở...)" value={al.reaction} onChange={(e) => handleUpdateAllergy(idx, 'reaction', e.target.value)} /></Col>
                            <Col xs={24} md={3}><Button htmlType="button" type="text" danger onClick={() => handleRemoveAllergy(idx)}><Trash2 size={16} /></Button></Col>
                        </Row>)}
                    </div>
                </div>}
                <div className={styles.nextStepFooter}><Button htmlType="button" type="primary" onClick={handleProceedToStep2}>Tiếp tục: Chuẩn bị lượt khám <ArrowRight size={16} /></Button></div>
            </Card>}
            {step === 2 && <Card className={styles.stepCard}>
                <h3 className={styles.stepTitle}>Thông tin phân luồng và phòng khám</h3>
                <Row gutter={[spacing.md, spacing.md]} className={styles.visitFormRow}>
                    <Col xs={24} md={12}><div className={styles.formField}><label className={styles.fieldLabel}>Cơ sở khám chữa bệnh *</label><select className={styles.nativeSelect} value={facilityId} onChange={(e) => setFacilityId(Number(e.target.value))}>{facilities.map(f => <option key={f.id} value={f.id}>{f.name} ({f.code})</option>)}</select></div></Col>
                    <Col xs={24} md={12}><div className={styles.formField}><label className={styles.fieldLabel}>Khoa phòng tiếp nhận *</label><Select className={styles.formSelect} value={departmentId || ''} onChange={(value) => setDepartmentId(Number(value))} options={departments.map(d => ({value:d.id,label:`${d.name} (${d.code})`}))} /></div></Col>
                </Row>
                <Row gutter={[spacing.md, spacing.md]} className={styles.visitFormRow}>
                    <Col xs={24} md={12}><div className={styles.formField}><label className={styles.fieldLabel}>Phòng bệnh / Buồng khám</label><Select className={styles.formSelect} value={roomId || ''} onChange={(value) => setRoomId(value ? Number(value) : undefined)} options={[{value:'',label:'-- Tự động xếp phòng trống --'},...rooms.map(r => ({value:r.id,label:`${r.roomNumber} - ${r.name}`}))]} /></div></Col>
                    <Col xs={24} md={12}><div className={styles.formField}><label className={styles.fieldLabel}>Bác sĩ phụ trách</label><select className={styles.nativeSelect} value={doctorId || ''} onChange={(e) => setDoctorId(e.target.value ? Number(e.target.value) : undefined)}><option value="">-- Tự động phân công theo ca trực --</option>{doctors.map(doc => <option key={doc.id} value={doc.id}>{doc.fullName} ({doc.specialtyName || 'Đa khoa'})</option>)}</select></div></Col>
                </Row>
                <Row gutter={[spacing.md, spacing.md]} className={styles.visitFormRow}>
                    <Col xs={24} md={12}><div className={styles.formField}><label className={styles.fieldLabel}>Mức độ ưu tiên khám *</label><Select className={styles.formSelect} value={priority} onChange={(value) => setPriority(value as VisitPriority)} options={[{value:'Normal',label:'Khám thường (Theo thứ tự hàng đợi)'},{value:'Priority',label:'Ưu tiên (Trẻ < 6T, Người già ≥ 75T, Phụ nữ mang thai)'},{value:'Urgent',label:'Khẩn cấp'},{value:'Emergency',label:'Cấp cứu'}]} /></div></Col>
                </Row>
                <div className={styles.chiefComplaintField}><label className={styles.fieldLabel}>Lý do khám / Triệu chứng ban đầu *</label><Input.TextArea rows={3} placeholder="Mô tả lý do đến khám, biểu hiện sốt, đau, ho, hoặc yêu cầu kiểm tra sức khỏe..." value={chiefComplaint} onChange={(e) => setChiefComplaint(e.target.value)} required /></div>
                <div className={styles.stepFooter}><Button htmlType="button" onClick={() => setStep(1)}><ArrowLeft size={16} /> Quay lại bước 1</Button><Button htmlType="button" type="primary" onClick={handleProceedToStep3}>Tiếp tục: Xác nhận thông tin <ArrowRight size={16} /></Button></div>
            </Card>}
            {step === 3 && <Card className={styles.stepCard}>
                <h3 className={styles.stepTitle}>Kiểm tra & Xác nhận thông tin lượt khám</h3>
                <div className={styles.patientSummary}><h4 className={styles.summaryTitle}>1. Thông tin người bệnh</h4>
                    <Row gutter={[spacing.md, spacing.sm]}>
                        <Col xs={24} md={12}><strong>Họ tên:</strong>{' '}{patientMode === 'lookup' ? selectedPatient?.fullName : newFullName}</Col>
                        <Col xs={24} md={12}><strong>Mã BN / MRN:</strong>{' '}{patientMode === 'lookup' ? (selectedPatient?.medicalRecordNumber || 'Hồ sơ cũ') : '(Hệ thống sẽ cấp tự động)'}</Col>
                        <Col xs={24} md={12}><strong>Số điện thoại:</strong>{' '}{patientMode === 'lookup' ? (selectedPatient?.phoneNumber || '---') : (newPhone || contactPhone || '---')}</Col>
                        <Col xs={24} md={12}><strong>Ngày sinh:</strong>{' '}{patientMode === 'lookup' ? selectedPatient?.dateOfBirth : newDateOfBirth}</Col>
                    </Row>
                </div>
                <div className={styles.visitSummary}><h4 className={styles.summaryTitle}>2. Thông tin tiếp nhận & Phòng khám</h4>
                    <Row gutter={[spacing.md, spacing.sm]}>
                        <Col xs={24} md={12}><strong>Cơ sở:</strong>{' '}{facilities.find(f => f.id === facilityId)?.name}</Col>
                        <Col xs={24} md={12}><strong>Khoa phòng:</strong>{' '}{departments.find(d => d.id === departmentId)?.name}</Col>
                        <Col xs={24} md={12}><strong>Buồng khám:</strong>{' '}{rooms.find(r => r.id === roomId)?.name || 'Tự động điều phối'}</Col>
                        <Col xs={24} md={12}><strong>Mức độ ưu tiên:</strong>{' '}<StatusBadge status={priority === 'Priority' ? 'pending' : priority === 'Emergency' ? 'cancelled' : 'confirmed'} label={priority === 'Priority' ? 'Ưu tiên' : priority === 'Emergency' ? 'Cấp cứu' : 'Thường'} /></Col>
                        <Col span={24}><strong>Lý do khám:</strong> {chiefComplaint}</Col>
                    </Row>
                </div>
                <div className={styles.stepFooter}><Button htmlType="button" onClick={() => setStep(2)} disabled={submitting}><ArrowLeft size={16} /> Quay lại sửa</Button><Button htmlType="button" type="primary" onClick={handleConfirmIntake} disabled={submitting} className={styles.confirmIntakeButton}>{submitting ? <RefreshCw className="spin" size={18} /> : <CheckCircle2 size={18} />}{submitting ? 'Đang tạo lượt khám...' : 'Xác nhận tiếp nhận & Cấp STT'}</Button></div>
            </Card>}
            <CheckInTicketModal isOpen={ticketModalOpen} ticket={currentTicket} onClose={() => { setTicketModalOpen(false); handleResetAll(); }} />
        </div>
    );
};
