import React, { useState, useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
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

    // 1. Initial Load: Facilities and Doctors
    useEffect(() => {
        organizationApi.getFacilities(false)
            .then(res => {
                if (res.success && res.data && res.data.length > 0) {
                    setFacilities(res.data);
                    setFacilityId(res.data[0].id);
                }
            })
            .catch(err => console.error('Lỗi tải cơ sở:', err));

        axiosClient.get<any, ApiResponse<Doctor[]>>('/doctors')
            .then(res => {
                if (res.success && res.data) {
                    setDoctors(res.data);
                }
            })
            .catch(err => console.error('Lỗi tải bác sĩ:', err));
    }, []);

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
                    identityCardNumber: newIdentityCard.trim() || undefined,
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
        <div style={{ maxWidth: '1000px', margin: '0 auto', paddingBottom: '60px' }}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <div>
                    <h1 style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--c-navy-dark)', margin: 0 }}>
                        Quy Trình Tiếp Nhận Người Bệnh Khám
                    </h1>
                    <p style={{ fontSize: '0.88rem', color: 'var(--c-muted)', marginTop: '4px' }}>
                        Quy trình 3 bước chuẩn: Tra cứu hồ sơ cũ ➔ Chuẩn bị lượt khám ➔ Cấp số thứ tự & In phiếu
                    </p>
                </div>
                <button
                    type="button"
                    className="btn-secondary"
                    onClick={() => navigate('/reception')}
                    style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                >
                    <ArrowLeft size={16} /> Bàn làm việc lễ tân
                </button>
            </div>

            {/* Stepper Navigation */}
            <div style={{
                display: 'grid',
                gridTemplateColumns: '1fr 1fr 1fr',
                gap: '12px',
                marginBottom: '28px',
                background: '#ffffff',
                padding: '12px',
                borderRadius: '12px',
                border: '1px solid var(--c-border)',
                boxShadow: 'var(--shadow-sm)'
            }}>
                <div style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    padding: '10px 14px',
                    borderRadius: '8px',
                    background: step === 1 ? '#e0f2fe' : '#f8fafc',
                    color: step === 1 ? '#0369a1' : '#64748b',
                    fontWeight: 600,
                    borderLeft: step === 1 ? '4px solid #0284c7' : '4px solid transparent'
                }}>
                    <div style={{ width: '26px', height: '26px', borderRadius: '50%', background: step >= 1 ? '#0284c7' : '#cbd5e1', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.85rem' }}>1</div>
                    <span style={{ fontSize: '0.9rem' }}>Tìm & Chọn Người Bệnh</span>
                </div>

                <div style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    padding: '10px 14px',
                    borderRadius: '8px',
                    background: step === 2 ? '#e0f2fe' : '#f8fafc',
                    color: step === 2 ? '#0369a1' : '#64748b',
                    fontWeight: 600,
                    borderLeft: step === 2 ? '4px solid #0284c7' : '4px solid transparent'
                }}>
                    <div style={{ width: '26px', height: '26px', borderRadius: '50%', background: step >= 2 ? '#0284c7' : '#cbd5e1', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.85rem' }}>2</div>
                    <span style={{ fontSize: '0.9rem' }}>Chuẩn Bị Lượt Khám</span>
                </div>

                <div style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    padding: '10px 14px',
                    borderRadius: '8px',
                    background: step === 3 ? '#e0f2fe' : '#f8fafc',
                    color: step === 3 ? '#0369a1' : '#64748b',
                    fontWeight: 600,
                    borderLeft: step === 3 ? '4px solid #0284c7' : '4px solid transparent'
                }}>
                    <div style={{ width: '26px', height: '26px', borderRadius: '50%', background: step >= 3 ? '#0284c7' : '#cbd5e1', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.85rem' }}>3</div>
                    <span style={{ fontSize: '0.9rem' }}>Xác Nhận & Cấp STT</span>
                </div>
            </div>

            {/* STEP 1: PATIENT LOOKUP OR REGISTRATION */}
            {step === 1 && (
                <div className="card" style={{ padding: '24px' }}>
                    {/* Patient Mode Toggle */}
                    <div style={{ display: 'flex', gap: '10px', marginBottom: '20px', borderBottom: '1px solid var(--c-border)', paddingBottom: '14px' }}>
                        <button
                            type="button"
                            onClick={() => setPatientMode('lookup')}
                            style={{
                                padding: '10px 20px',
                                borderRadius: '8px',
                                border: 'none',
                                background: patientMode === 'lookup' ? '#0284c7' : '#f1f5f9',
                                color: patientMode === 'lookup' ? '#ffffff' : '#475569',
                                fontWeight: 600,
                                cursor: 'pointer',
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px'
                            }}
                        >
                            <Search size={16} /> Tra cứu hồ sơ cũ (Khuyên dùng)
                        </button>
                        <button
                            type="button"
                            onClick={() => setPatientMode('new')}
                            style={{
                                padding: '10px 20px',
                                borderRadius: '8px',
                                border: 'none',
                                background: patientMode === 'new' ? '#0284c7' : '#f1f5f9',
                                color: patientMode === 'new' ? '#ffffff' : '#475569',
                                fontWeight: 600,
                                cursor: 'pointer',
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px'
                            }}
                        >
                            <UserPlus size={16} /> Đăng ký hồ sơ người bệnh mới
                        </button>
                    </div>

                    {/* Mode A: Lookup Existing Patient */}
                    {patientMode === 'lookup' && (
                        <div>
                            <form onSubmit={handleSearchPatient} style={{ display: 'flex', gap: '10px', marginBottom: '20px' }}>
                                <input
                                    type="text"
                                    className="form-input"
                                    style={{ flex: 1, fontSize: '0.95rem' }}
                                    placeholder="Nhập Mã bệnh nhân (MRN), Số CCCD, Số điện thoại hoặc Họ tên..."
                                    value={searchQuery}
                                    onChange={(e) => setSearchQuery(e.target.value)}
                                />
                                <button type="submit" className="btn-primary" disabled={searching} style={{ padding: '0 24px' }}>
                                    {searching ? <RefreshCw className="spin" size={16} /> : <Search size={16} />} Tìm hồ sơ
                                </button>
                            </form>

                            {/* Selected Patient Banner */}
                            {selectedPatient && (
                                <div style={{
                                    background: '#f0fdf4',
                                    border: '2px solid #86efac',
                                    borderRadius: '12px',
                                    padding: '18px 20px',
                                    marginBottom: '20px'
                                }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                            <CheckCircle2 size={24} color="#16a34a" />
                                            <div>
                                                <h3 style={{ margin: 0, fontSize: '1.15rem', color: '#15803d', fontWeight: 700 }}>
                                                    {selectedPatient.fullName}
                                                </h3>
                                                <div style={{ fontSize: '0.85rem', color: '#166534', marginTop: '2px' }}>
                                                    Mã bệnh nhân (MRN): <strong>{selectedPatient.medicalRecordNumber || 'Chưa gán'}</strong> • CCCD: {selectedPatient.nationalId || '---'}
                                                </div>
                                            </div>
                                        </div>
                                        <button
                                            type="button"
                                            className="btn-secondary"
                                            onClick={() => setSelectedPatient(null)}
                                            style={{ fontSize: '0.8rem', padding: '4px 10px' }}
                                        >
                                            Chọn hồ sơ khác
                                        </button>
                                    </div>

                                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '12px', marginTop: '14px', paddingTop: '12px', borderTop: '1px solid #bbf7d0', fontSize: '0.85rem', color: '#166534' }}>
                                        <div><strong>Ngày sinh:</strong> {selectedPatient.dateOfBirth || '---'}</div>
                                        <div><strong>Giới tính:</strong> {selectedPatient.genderName || selectedPatient.gender || '---'}</div>
                                        <div><strong>Số điện thoại:</strong> {selectedPatient.phoneNumber || '---'}</div>
                                        <div><strong>Địa chỉ:</strong> {selectedPatient.address || '---'}</div>
                                    </div>
                                </div>
                            )}

                            {/* Search Results List */}
                            {!selectedPatient && searchResults.length > 0 && (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', marginBottom: '20px' }}>
                                    <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', fontWeight: 600 }}>
                                        Tìm thấy {searchResults.length} hồ sơ người bệnh phù hợp:
                                    </div>
                                    {searchResults.map(p => (
                                        <div
                                            key={p.id}
                                            onClick={() => setSelectedPatient(p)}
                                            style={{
                                                border: '1px solid #cbd5e1',
                                                borderRadius: '8px',
                                                padding: '14px 16px',
                                                cursor: 'pointer',
                                                background: '#ffffff',
                                                display: 'flex',
                                                justifyContent: 'space-between',
                                                alignItems: 'center',
                                                transition: 'all 0.15s ease'
                                            }}
                                            onMouseEnter={(e) => (e.currentTarget.style.borderColor = '#0284c7')}
                                            onMouseLeave={(e) => (e.currentTarget.style.borderColor = '#cbd5e1')}
                                        >
                                            <div>
                                                <div style={{ fontWeight: 600, color: 'var(--c-navy-dark)', fontSize: '0.95rem' }}>
                                                    {p.fullName} <span style={{ color: '#0284c7', fontSize: '0.85rem', fontWeight: 500 }}>({p.medicalRecordNumber || 'Chưa có MRN'})</span>
                                                </div>
                                                <div style={{ fontSize: '0.82rem', color: 'var(--c-muted)', marginTop: '3px' }}>
                                                    CCCD: {p.nationalId || '---'} • SĐT: {p.phoneNumber || '---'} • Sinh: {p.dateOfBirth || '---'} ({p.genderName || p.gender})
                                                </div>
                                            </div>
                                            <button type="button" className="btn-primary" style={{ padding: '6px 14px', fontSize: '0.82rem' }}>
                                                <UserCheck size={14} /> Chọn hồ sơ này
                                            </button>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    )}

                    {/* Mode B: New Patient Entry Form */}
                    {patientMode === 'new' && (
                        <div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
                                <div className="form-group">
                                    <label className="form-label">Họ và tên người bệnh *</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        placeholder="VD: NGUYỄN VĂN A"
                                        value={newFullName}
                                        onChange={(e) => setNewFullName(e.target.value)}
                                        required
                                    />
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Số CCCD / CMND</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        placeholder="12 chữ số CCCD..."
                                        value={newIdentityCard}
                                        onChange={(e) => setNewIdentityCard(e.target.value)}
                                    />
                                </div>
                            </div>

                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '16px', marginBottom: '16px' }}>
                                <div className="form-group">
                                    <label className="form-label">Ngày sinh *</label>
                                    <input
                                        type="date"
                                        className="form-input"
                                        value={newDateOfBirth}
                                        onChange={(e) => setNewDateOfBirth(e.target.value)}
                                        required
                                    />
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Giới tính *</label>
                                    <select
                                        className="form-select"
                                        value={newGender}
                                        onChange={(e) => setNewGender(Number(e.target.value))}
                                    >
                                        <option value={0}>Nam</option>
                                        <option value={1}>Nữ</option>
                                        <option value={2}>Khác</option>
                                    </select>
                                </div>

                                <div className="form-group">
                                    <label className="form-label">
                                        Số điện thoại cá nhân
                                        <span style={{ fontSize: '0.75rem', color: 'var(--c-muted)', display: 'block' }}>
                                            (Tùy chọn nếu có SĐT khẩn cấp)
                                        </span>
                                    </label>
                                    <input
                                        type="tel"
                                        className="form-input"
                                        placeholder="09xx xxx xxx"
                                        value={newPhone}
                                        onChange={(e) => setNewPhone(e.target.value)}
                                    />
                                </div>
                            </div>

                            <div className="form-group" style={{ marginBottom: '20px' }}>
                                <label className="form-label">Địa chỉ cư trú</label>
                                <input
                                    type="text"
                                    className="form-input"
                                    placeholder="Số nhà, tên đường, phường/xã, quận/huyện, tỉnh/thành..."
                                    value={newAddress}
                                    onChange={(e) => setNewAddress(e.target.value)}
                                />
                            </div>

                            {/* Emergency Contact */}
                            <div style={{ borderTop: '1px solid var(--c-border)', paddingTop: '16px', marginBottom: '20px' }}>
                                <h4 style={{ fontSize: '0.95rem', fontWeight: 600, color: 'var(--c-navy-dark)', marginBottom: '12px' }}>
                                    Người liên hệ khẩn cấp / Giám hộ (Bắt buộc nếu người bệnh không có SĐT)
                                </h4>
                                <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr 1.2fr', gap: '16px' }}>
                                    <div className="form-group">
                                        <label className="form-label">Họ tên người liên hệ</label>
                                        <input
                                            type="text"
                                            className="form-input"
                                            placeholder="VD: Trần Thị B"
                                            value={contactName}
                                            onChange={(e) => setContactName(e.target.value)}
                                        />
                                    </div>
                                    <div className="form-group">
                                        <label className="form-label">Mối quan hệ</label>
                                        <select
                                            className="form-select"
                                            value={contactRelationship}
                                            onChange={(e) => setContactRelationship(e.target.value)}
                                        >
                                            <option value="Bố/Mẹ">Bố/Mẹ</option>
                                            <option value="Vợ/Chồng">Vợ/Chồng</option>
                                            <option value="Con cái">Con cái</option>
                                            <option value="Người thân">Người thân khác</option>
                                            <option value="Người giám hộ">Người giám hộ</option>
                                        </select>
                                    </div>
                                    <div className="form-group">
                                        <label className="form-label">Số điện thoại liên hệ</label>
                                        <input
                                            type="tel"
                                            className="form-input"
                                            placeholder="09xx xxx xxx"
                                            value={contactPhone}
                                            onChange={(e) => setContactPhone(e.target.value)}
                                        />
                                    </div>
                                </div>
                            </div>

                            {/* Allergies list */}
                            <div style={{ borderTop: '1px solid var(--c-border)', paddingTop: '16px' }}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                    <h4 style={{ fontSize: '0.95rem', fontWeight: 600, color: '#dc2626', margin: 0, display: 'flex', alignItems: 'center', gap: '6px' }}>
                                        <ShieldAlert size={16} /> Tiền sử dị ứng thuốc & thực phẩm
                                    </h4>
                                    <button
                                        type="button"
                                        className="btn-secondary"
                                        style={{ fontSize: '0.8rem', padding: '4px 10px' }}
                                        onClick={handleAddAllergy}
                                    >
                                        <Plus size={13} /> Thêm dị ứng
                                    </button>
                                </div>

                                {allergies.map((al, idx) => (
                                    <div key={idx} style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 2fr auto', gap: '10px', alignItems: 'center', marginBottom: '8px' }}>
                                        <input
                                            type="text"
                                            className="form-input"
                                            placeholder="Dị nguyên (VD: Penicillin, Paracetamol...)"
                                            value={al.allergen}
                                            onChange={(e) => handleUpdateAllergy(idx, 'allergen', e.target.value)}
                                        />
                                        <select
                                            className="form-select"
                                            value={al.severity}
                                            onChange={(e) => handleUpdateAllergy(idx, 'severity', e.target.value)}
                                        >
                                            <option value="Mild">Nhẹ</option>
                                            <option value="Moderate">Vừa</option>
                                            <option value="Severe">Nặng / Sốc phản vệ</option>
                                        </select>
                                        <input
                                            type="text"
                                            className="form-input"
                                            placeholder="Phản ứng (VD: Nổi mẩn, khó thở...)"
                                            value={al.reaction}
                                            onChange={(e) => handleUpdateAllergy(idx, 'reaction', e.target.value)}
                                        />
                                        <button
                                            type="button"
                                            onClick={() => handleRemoveAllergy(idx)}
                                            style={{ border: 'none', background: 'transparent', color: '#ef4444', cursor: 'pointer', padding: '4px' }}
                                        >
                                            <Trash2 size={16} />
                                        </button>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {/* Step 1 Footer */}
                    <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '24px', borderTop: '1px solid var(--c-border)', paddingTop: '16px' }}>
                        <button
                            type="button"
                            className="btn-primary"
                            onClick={handleProceedToStep2}
                            style={{ padding: '10px 24px', display: 'flex', alignItems: 'center', gap: '8px' }}
                        >
                            Tiếp tục: Chuẩn bị lượt khám <ArrowRight size={16} />
                        </button>
                    </div>
                </div>
            )}

            {/* STEP 2: VISIT PREPARATION */}
            {step === 2 && (
                <div className="card" style={{ padding: '24px' }}>
                    <h3 style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--c-navy-dark)', marginBottom: '16px' }}>
                        Thông tin phân luồng và phòng khám
                    </h3>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
                        <div className="form-group">
                            <label className="form-label">Cơ sở khám chữa bệnh *</label>
                            <select
                                className="form-select"
                                value={facilityId}
                                onChange={(e) => setFacilityId(Number(e.target.value))}
                            >
                                {facilities.map(f => (
                                    <option key={f.id} value={f.id}>{f.name} ({f.code})</option>
                                ))}
                            </select>
                        </div>

                        <div className="form-group">
                            <label className="form-label">Khoa phòng tiếp nhận *</label>
                            <select
                                className="form-select"
                                value={departmentId || ''}
                                onChange={(e) => setDepartmentId(Number(e.target.value))}
                            >
                                {departments.map(d => (
                                    <option key={d.id} value={d.id}>{d.name} ({d.code})</option>
                                ))}
                            </select>
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
                        <div className="form-group">
                            <label className="form-label">Phòng bệnh / Buồng khám</label>
                            <select
                                className="form-select"
                                value={roomId || ''}
                                onChange={(e) => setRoomId(e.target.value ? Number(e.target.value) : undefined)}
                            >
                                <option value="">-- Tự động xếp phòng trống --</option>
                                {rooms.map(r => (
                                    <option key={r.id} value={r.id}>{r.roomNumber} - {r.name}</option>
                                ))}
                            </select>
                        </div>

                        <div className="form-group">
                            <label className="form-label">Bác sĩ phụ trách</label>
                            <select
                                className="form-select"
                                value={doctorId || ''}
                                onChange={(e) => setDoctorId(e.target.value ? Number(e.target.value) : undefined)}
                            >
                                <option value="">-- Tự động phân công theo ca trực --</option>
                                {doctors.map(doc => (
                                    <option key={doc.id} value={doc.id}>
                                        {doc.fullName} ({doc.specialtyName || 'Đa khoa'})
                                    </option>
                                ))}
                            </select>
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
                        <div className="form-group">
                            <label className="form-label">Mức độ ưu tiên khám *</label>
                            <select
                                className="form-select"
                                value={priority}
                                onChange={(e) => setPriority(e.target.value as VisitPriority)}
                            >
                                <option value="Normal">Khám thường (Theo thứ tự hàng đợi)</option>
                                <option value="Priority">Ưu tiên (Trẻ &lt; 6T, Người già &ge; 75T, Phụ nữ mang thai)</option>
                                <option value="Urgent">Khẩn cấp</option>
                                <option value="Emergency">Cấp cứu</option>
                            </select>
                        </div>
                    </div>

                    <div className="form-group" style={{ marginBottom: '24px' }}>
                        <label className="form-label">Lý do khám / Triệu chứng ban đầu *</label>
                        <textarea
                            className="form-input"
                            rows={3}
                            placeholder="Mô tả lý do đến khám, biểu hiện sốt, đau, ho, hoặc yêu cầu kiểm tra sức khỏe..."
                            value={chiefComplaint}
                            onChange={(e) => setChiefComplaint(e.target.value)}
                            required
                        />
                    </div>

                    {/* Step 2 Footer */}
                    <div style={{ display: 'flex', justifyContent: 'space-between', borderTop: '1px solid var(--c-border)', paddingTop: '16px' }}>
                        <button
                            type="button"
                            className="btn-secondary"
                            onClick={() => setStep(1)}
                            style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                        >
                            <ArrowLeft size={16} /> Quay lại bước 1
                        </button>
                        <button
                            type="button"
                            className="btn-primary"
                            onClick={handleProceedToStep3}
                            style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                        >
                            Tiếp tục: Xác nhận thông tin <ArrowRight size={16} />
                        </button>
                    </div>
                </div>
            )}

            {/* STEP 3: CONFIRMATION AND PRINT TICKET */}
            {step === 3 && (
                <div className="card" style={{ padding: '24px' }}>
                    <h3 style={{ fontSize: '1.15rem', fontWeight: 700, color: 'var(--c-navy-dark)', marginBottom: '16px' }}>
                        Kiểm tra & Xác nhận thông tin lượt khám
                    </h3>

                    {/* Patient Summary Review */}
                    <div style={{ background: '#f8fafc', border: '1px solid var(--c-border)', borderRadius: '10px', padding: '18px', marginBottom: '18px' }}>
                        <h4 style={{ margin: 0, fontSize: '0.95rem', fontWeight: 600, color: 'var(--c-navy-dark)', marginBottom: '12px' }}>
                            1. Thông tin người bệnh
                        </h4>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '10px', fontSize: '0.88rem' }}>
                            <div>
                                <strong>Họ tên:</strong>{' '}
                                {patientMode === 'lookup' ? selectedPatient?.fullName : newFullName}
                            </div>
                            <div>
                                <strong>Mã BN / MRN:</strong>{' '}
                                {patientMode === 'lookup' ? (selectedPatient?.medicalRecordNumber || 'Hồ sơ cũ') : '(Hệ thống sẽ cấp tự động)'}
                            </div>
                            <div>
                                <strong>Số điện thoại:</strong>{' '}
                                {patientMode === 'lookup' ? (selectedPatient?.phoneNumber || '---') : (newPhone || contactPhone || '---')}
                            </div>
                            <div>
                                <strong>Ngày sinh:</strong>{' '}
                                {patientMode === 'lookup' ? selectedPatient?.dateOfBirth : newDateOfBirth}
                            </div>
                        </div>
                    </div>

                    {/* Visit Summary Review */}
                    <div style={{ background: '#f8fafc', border: '1px solid var(--c-border)', borderRadius: '10px', padding: '18px', marginBottom: '24px' }}>
                        <h4 style={{ margin: 0, fontSize: '0.95rem', fontWeight: 600, color: 'var(--c-navy-dark)', marginBottom: '12px' }}>
                            2. Thông tin tiếp nhận & Phòng khám
                        </h4>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '10px', fontSize: '0.88rem' }}>
                            <div>
                                <strong>Cơ sở:</strong>{' '}
                                {facilities.find(f => f.id === facilityId)?.name}
                            </div>
                            <div>
                                <strong>Khoa phòng:</strong>{' '}
                                {departments.find(d => d.id === departmentId)?.name}
                            </div>
                            <div>
                                <strong>Buồng khám:</strong>{' '}
                                {rooms.find(r => r.id === roomId)?.name || 'Tự động điều phối'}
                            </div>
                            <div>
                                <strong>Mức độ ưu tiên:</strong>{' '}
                                <span className={priority === 'Priority' ? 'badge badge-warning' : priority === 'Emergency' ? 'badge badge-danger' : 'badge badge-info'}>
                                    {priority === 'Priority' ? 'Ưu tiên' : priority === 'Emergency' ? 'Cấp cứu' : 'Thường'}
                                </span>
                            </div>
                            <div style={{ gridColumn: '1 / -1' }}>
                                <strong>Lý do khám:</strong> {chiefComplaint}
                            </div>
                        </div>
                    </div>

                    {/* Step 3 Footer */}
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderTop: '1px solid var(--c-border)', paddingTop: '16px' }}>
                        <button
                            type="button"
                            className="btn-secondary"
                            onClick={() => setStep(2)}
                            disabled={submitting}
                            style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                        >
                            <ArrowLeft size={16} /> Quay lại sửa
                        </button>
                        <button
                            type="button"
                            className="btn-primary"
                            onClick={handleConfirmIntake}
                            disabled={submitting}
                            style={{ padding: '12px 28px', fontSize: '0.95rem', fontWeight: 700, display: 'flex', alignItems: 'center', gap: '8px' }}
                        >
                            {submitting ? <RefreshCw className="spin" size={18} /> : <CheckCircle2 size={18} />}
                            {submitting ? 'Đang tạo lượt khám...' : 'Xác nhận tiếp nhận & Cấp STT'}
                        </button>
                    </div>
                </div>
            )}

            {/* Check-In Ticket Modal */}
            <CheckInTicketModal
                isOpen={ticketModalOpen}
                ticket={currentTicket}
                onClose={() => {
                    setTicketModalOpen(false);
                    handleResetAll();
                }}
            />
        </div>
    );
};
