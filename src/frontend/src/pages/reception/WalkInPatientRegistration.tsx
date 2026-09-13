import React, { useState, useEffect } from 'react';
import { 
    UserPlus, Search, ShieldAlert, HeartPulse, User, Phone, 
    Plus, Trash2, CheckCircle2, Printer, RotateCcw, Ticket
} from 'lucide-react';
import { mpiApi, type RegisterWalkInPatientPayload, type MpiPatientDto, type GenderType } from '../../api/mpiApi';
import { organizationApi, type FacilityDto, type DepartmentDto, type RoomDto } from '../../api/organizationApi';
import { patientVisitApi } from '../../api/patientVisitApi';
import type { CheckInTicketDto, VisitPriority } from '../../types';
import { CheckInTicketModal } from '../../components/CheckInTicketModal';
import { useDialog } from '../../contexts/DialogContext';
import { MpiPatientSearchModal } from './MpiPatientSearchModal';

interface AllergyItem {
    allergenType: number;
    allergenName: string;
    severity: number;
    reactionDescription: string;
}

export const WalkInPatientRegistration: React.FC = () => {
    const { showAlert } = useDialog();

    // Facilities
    const [facilities, setFacilities] = useState<FacilityDto[]>([]);
    const [loadingFacilities, setLoadingFacilities] = useState(false);

    // Search modal state
    const [searchModalOpen, setSearchModalOpen] = useState(false);

    // Form state
    const [fullName, setFullName] = useState('');
    const [phoneNumber, setPhoneNumber] = useState('');
    const [email, setEmail] = useState('');
    const [gender, setGender] = useState<GenderType>('Male');
    const [dateOfBirth, setDateOfBirth] = useState('');
    const [address, setAddress] = useState('');
    const [nationalId, setNationalId] = useState('');
    const [bhytNumber, setBhytNumber] = useState('');
    const [bloodType, setBloodType] = useState('');
    const [rhFactor, setRhFactor] = useState('');
    const [primaryFacilityId, setPrimaryFacilityId] = useState<number | undefined>(undefined);

    // Emergency Contact
    const [contactName, setContactName] = useState('');
    const [contactRelationship, setContactRelationship] = useState('Người thân');
    const [contactPhone, setContactPhone] = useState('');
    const [contactAddress, setContactAddress] = useState('');

    // Allergies list
    const [allergies, setAllergies] = useState<AllergyItem[]>([]);

    // Outpatient Department & Queueing (Phase 1.2 Connected Journey)
    const [issueQueueTicket, setIssueQueueTicket] = useState(false);
    const [departments, setDepartments] = useState<DepartmentDto[]>([]);
    const [rooms, setRooms] = useState<RoomDto[]>([]);
    const [selectedDepartmentId, setSelectedDepartmentId] = useState<number | undefined>(undefined);
    const [selectedRoomId, setSelectedRoomId] = useState<number | undefined>(undefined);
    const [selectedDoctorId, setSelectedDoctorId] = useState<number | undefined>(undefined);
    const [chiefComplaint, setChiefComplaint] = useState('');
    const [priority, setPriority] = useState<VisitPriority>('Normal');

    // Submission & Success
    const [submitting, setSubmitting] = useState(false);
    const [registeredPatient, setRegisteredPatient] = useState<MpiPatientDto | null>(null);
    const [currentTicket, setCurrentTicket] = useState<CheckInTicketDto | null>(null);
    const [ticketModalOpen, setTicketModalOpen] = useState(false);

    useEffect(() => {
        loadFacilities();
    }, []);

    useEffect(() => {
        if (primaryFacilityId) {
            organizationApi.getDepartments(primaryFacilityId).then(res => {
                if (res.success && res.data) {
                    setDepartments(res.data);
                    if (res.data.length > 0) {
                        setSelectedDepartmentId(res.data[0].id);
                    } else {
                        setSelectedDepartmentId(undefined);
                    }
                }
            }).catch(err => console.error('Lỗi tải danh sách khoa:', err));
        }
    }, [primaryFacilityId]);

    useEffect(() => {
        if (selectedDepartmentId) {
            organizationApi.getRooms({ facilityId: primaryFacilityId, departmentId: selectedDepartmentId }).then(res => {
                if (res.success && res.data) {
                    setRooms(res.data);
                    setSelectedRoomId(res.data.length > 0 ? res.data[0].id : undefined);
                }
            }).catch(err => console.error('Lỗi tải danh sách phòng:', err));
        } else {
            setRooms([]);
            setSelectedRoomId(undefined);
        }
    }, [selectedDepartmentId, primaryFacilityId]);

    const loadFacilities = async () => {
        setLoadingFacilities(true);
        try {
            const res = await organizationApi.getFacilities(true);
            if (res.success && res.data) {
                setFacilities(res.data);
                if (res.data.length > 0) {
                    setPrimaryFacilityId(res.data[0].id);
                }
            }
        } catch (err) {
            console.error('Không thể tải danh sách cơ sở:', err);
        } finally {
            setLoadingFacilities(false);
        }
    };

    const handleAddAllergy = () => {
        setAllergies([
            ...allergies,
            { allergenType: 1, allergenName: '', severity: 1, reactionDescription: '' }
        ]);
    };

    const handleUpdateAllergy = (index: number, field: keyof AllergyItem, value: any) => {
        const updated = [...allergies];
        updated[index] = { ...updated[index], [field]: value };
        setAllergies(updated);
    };

    const handleRemoveAllergy = (index: number) => {
        setAllergies(allergies.filter((_, i) => i !== index));
    };

    const handleSelectExistingPatient = (patient: MpiPatientDto) => {
        showAlert(
            'Bệnh nhân đã có mã MRN!',
            `Bệnh nhân ${patient.fullName} đã tồn tại trong hệ thống với Mã bệnh án ${patient.medicalRecordNumber}. Vui lòng chuyển sang luồng Đặt lịch khám hoặc Thu ngân.`,
            'info'
        );
    };

    const resetForm = () => {
        setFullName('');
        setPhoneNumber('');
        setEmail('');
        setGender('Male');
        setDateOfBirth('');
        setAddress('');
        setNationalId('');
        setBhytNumber('');
        setBloodType('');
        setRhFactor('');
        setContactName('');
        setContactRelationship('Người thân');
        setContactPhone('');
        setContactAddress('');
        setAllergies([]);
        setChiefComplaint('');
        setPriority('Normal');
        setSelectedDoctorId(undefined);
        setRegisteredPatient(null);
        setCurrentTicket(null);
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        if (!fullName.trim()) {
            showAlert('Lỗi nhập liệu', 'Vui lòng nhập Họ và tên bệnh nhân.', 'error');
            return;
        }

        // Validate allergies
        for (const al of allergies) {
            if (!al.allergenName.trim()) {
                showAlert('Lỗi dị ứng', 'Vui lòng nhập tên tác nhân gây dị ứng.', 'warning');
                return;
            }
        }

        if (issueQueueTicket) {
            if (!phoneNumber.trim()) {
                showAlert('Thiếu số điện thoại', 'Vui lòng nhập số điện thoại bệnh nhân để liên hệ và cấp số thứ tự.', 'warning');
                return;
            }
            if (!selectedDepartmentId) {
                showAlert('Thiếu khoa khám', 'Vui lòng chọn Khoa / Chuyên khoa tiếp nhận bệnh nhân.', 'warning');
                return;
            }
            if (!chiefComplaint.trim()) {
                showAlert('Thiếu lý do khám', 'Vui lòng nhập Lý do khám / Triệu chứng ban đầu của bệnh nhân.', 'warning');
                return;
            }
        }

        setSubmitting(true);
        try {
            if (issueQueueTicket && selectedDepartmentId) {
                // Phase 1.2 Connected Outpatient Journey: Direct Walk-in into Clinic Queue with STT
                const res = await patientVisitApi.createWalkInVisit({
                    facilityId: primaryFacilityId || 1,
                    departmentId: selectedDepartmentId,
                    roomId: selectedRoomId,
                    assignedDoctorId: selectedDoctorId,
                    fullName: fullName.trim(),
                    phoneNumber: phoneNumber.trim(),
                    dateOfBirth: dateOfBirth || undefined,
                    gender: gender,
                    address: address.trim() || undefined,
                    chiefComplaint: chiefComplaint.trim(),
                    priority: priority
                });

                if (res.success && res.data) {
                    setCurrentTicket(res.data);
                    setTicketModalOpen(true);
                    showAlert(
                        'Tiếp nhận & Cấp STT thành công!',
                        `Đã cấp STT ${res.data.queueNumber} tại ${res.data.departmentName || 'phòng khám'} cho bệnh nhân ${res.data.patientName}. Mã bệnh án: ${res.data.medicalRecordNumber}.`,
                        'success'
                    );
                } else {
                    showAlert('Lỗi', res.message || 'Không thể tiếp nhận bệnh nhân.', 'error');
                }
            } else {
                // Fallback: MPI-only registration
                const payload: RegisterWalkInPatientPayload = {
                    fullName: fullName.trim(),
                    phoneNumber: phoneNumber.trim() || undefined,
                    email: email.trim() || undefined,
                    gender: gender,
                    dateOfBirth: dateOfBirth || undefined,
                    address: address.trim() || undefined,
                    nationalId: nationalId.trim() || undefined,
                    bhytNumber: bhytNumber.trim() || undefined,
                    bloodType: bloodType || undefined,
                    rhFactor: rhFactor || undefined,
                    primaryFacilityId: primaryFacilityId,
                    allergies: allergies.map(a => ({
                        allergenType: a.allergenType,
                        allergenName: a.allergenName.trim(),
                        severity: a.severity,
                        reactionDescription: a.reactionDescription.trim() || undefined
                    })),
                    emergencyContact: contactName.trim() ? {
                        fullName: contactName.trim(),
                        relationship: contactRelationship,
                        phoneNumber: contactPhone.trim(),
                        address: contactAddress.trim() || undefined
                    } : undefined
                };

                const res = await mpiApi.registerWalkIn(payload);
                if (res.success && res.data) {
                    setRegisteredPatient(res.data);
                    showAlert(
                        'Đăng ký thành công!',
                        `Đã cấp Mã bệnh án (MRN): ${res.data.medicalRecordNumber} cho bệnh nhân ${res.data.fullName}.`,
                        'success'
                    );
                } else {
                    showAlert('Lỗi', res.message || 'Không thể đăng ký bệnh nhân.', 'error');
                }
            }
        } catch (err: any) {
            const message = err.response?.data?.message || err.message || 'Lỗi hệ thống khi đăng ký bệnh nhân.';
            showAlert('Lỗi đăng ký', message, 'error');
        } finally {
            setSubmitting(false);
        }
    };

    return (
        <div style={{ padding: '24px', maxWidth: '1200px', margin: '0 auto' }}>
            {/* Top Bar */}
            <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: '24px',
                flexWrap: 'wrap',
                gap: '16px'
            }}>
                <div>
                    <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: '#0f172a', margin: '0 0 6px 0' }}>
                        Tiếp nhận bệnh nhân vãng lai (Walk-in)
                    </h1>
                    <p style={{ margin: 0, color: '#64748b', fontSize: '0.95rem' }}>
                        Cấp Mã bệnh án chuẩn hóa (MRN) tự động và khởi tạo hồ sơ Master Patient Index (MPI)
                    </p>
                </div>
                <div style={{ display: 'flex', gap: '12px' }}>
                    <button
                        type="button"
                        onClick={() => setSearchModalOpen(true)}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            padding: '10px 18px',
                            backgroundColor: '#0284c7',
                            color: '#fff',
                            border: 'none',
                            borderRadius: '8px',
                            fontWeight: 500,
                            cursor: 'pointer'
                        }}
                    >
                        <Search size={18} />
                        Tra cứu MPI (Tránh trùng mã)
                    </button>
                    {(registeredPatient || currentTicket) && (
                        <button
                            type="button"
                            onClick={resetForm}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '10px 18px',
                                backgroundColor: '#f1f5f9',
                                color: '#334155',
                                border: '1px solid #cbd5e1',
                                borderRadius: '8px',
                                fontWeight: 500,
                                cursor: 'pointer'
                            }}
                        >
                            <RotateCcw size={18} />
                            Tiếp nhận bệnh nhân khác
                        </button>
                    )}
                </div>
            </div>

            {/* Success Card if patient registered or ticket issued */}
            {currentTicket && (
                <div style={{
                    backgroundColor: '#ecfdf5',
                    border: '1px solid #10b981',
                    borderRadius: '12px',
                    padding: '24px',
                    marginBottom: '24px'
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '16px' }}>
                        <CheckCircle2 size={32} color="#059669" />
                        <div>
                            <h3 style={{ margin: 0, color: '#065f46', fontSize: '1.25rem', fontWeight: 700 }}>
                                Tiếp nhận & Phát số thứ tự (STT) thành công!
                            </h3>
                            <p style={{ margin: 0, color: '#047857', fontSize: '0.9rem' }}>
                                Bệnh nhân đã được đưa vào hàng đợi phòng khám của bác sĩ.
                            </p>
                        </div>
                    </div>

                    <div style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                        gap: '16px',
                        backgroundColor: '#fff',
                        padding: '16px',
                        borderRadius: '8px',
                        border: '1px solid #a7f3d0'
                    }}>
                        <div style={{ backgroundColor: '#f0fdf4', padding: '10px 14px', borderRadius: '8px', border: '1px solid #bbf7d0' }}>
                            <span style={{ fontSize: '0.8rem', color: '#15803d', fontWeight: 600 }}>SỐ THỨ TỰ (STT)</span>
                            <div style={{ fontSize: '1.8rem', fontWeight: 900, color: '#15803d' }}>
                                {currentTicket.queueNumber}
                            </div>
                        </div>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>MÃ BỆNH ÁN (MRN)</span>
                            <div style={{ fontSize: '1.2rem', fontWeight: 800, color: '#0284c7' }}>
                                {currentTicket.medicalRecordNumber}
                            </div>
                        </div>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>HỌ VÀ TÊN</span>
                            <div style={{ fontSize: '1.1rem', fontWeight: 600, color: '#0f172a' }}>
                                {currentTicket.patientName}
                            </div>
                        </div>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>KHOA / CHUYÊN KHOA</span>
                            <div style={{ fontSize: '1rem', fontWeight: 600, color: '#0f172a' }}>
                                {currentTicket.departmentName}
                            </div>
                        </div>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>PHÒNG KHÁM & BÁC SĨ</span>
                            <div style={{ fontSize: '0.95rem', fontWeight: 500, color: '#0f172a' }}>
                                {currentTicket.roomNumber ? `Phòng ${currentTicket.roomNumber}` : 'Phòng khám chung'} 
                                {currentTicket.doctorName ? ` • ${currentTicket.doctorName}` : ''}
                            </div>
                        </div>
                    </div>

                    <div style={{ display: 'flex', gap: '12px', marginTop: '16px' }}>
                        <button
                            type="button"
                            onClick={() => setTicketModalOpen(true)}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '10px 20px',
                                backgroundColor: '#059669',
                                color: '#fff',
                                border: 'none',
                                borderRadius: '6px',
                                fontWeight: 600,
                                cursor: 'pointer'
                            }}
                        >
                            <Printer size={18} />
                            In phiếu tiếp nhận khám (STT)
                        </button>
                    </div>
                </div>
            )}

            {!currentTicket && registeredPatient && (
                <div style={{
                    backgroundColor: '#ecfdf5',
                    border: '1px solid #10b981',
                    borderRadius: '12px',
                    padding: '24px',
                    marginBottom: '24px'
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '16px' }}>
                        <CheckCircle2 size={32} color="#059669" />
                        <div>
                            <h3 style={{ margin: 0, color: '#065f46', fontSize: '1.25rem', fontWeight: 700 }}>
                                Tiếp nhận bệnh nhân thành công!
                            </h3>
                            <p style={{ margin: 0, color: '#047857', fontSize: '0.9rem' }}>
                                Hồ sơ đã được lưu trữ trong danh bạ bệnh nhân toàn viện (MPI).
                            </p>
                        </div>
                    </div>

                    <div style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
                        gap: '16px',
                        backgroundColor: '#fff',
                        padding: '16px',
                        borderRadius: '8px',
                        border: '1px solid #a7f3d0'
                    }}>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>MÃ BỆNH ÁN (MRN)</span>
                            <div style={{ fontSize: '1.3rem', fontWeight: 800, color: '#0284c7' }}>
                                {registeredPatient.medicalRecordNumber}
                            </div>
                        </div>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>HỌ VÀ TÊN</span>
                            <div style={{ fontSize: '1.1rem', fontWeight: 600, color: '#0f172a' }}>
                                {registeredPatient.fullName}
                            </div>
                        </div>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>SỐ ĐIỆN THOẠI</span>
                            <div style={{ fontSize: '1rem', fontWeight: 500, color: '#0f172a' }}>
                                {registeredPatient.phoneNumber || 'Không có'}
                            </div>
                        </div>
                        <div>
                            <span style={{ fontSize: '0.8rem', color: '#64748b' }}>CƠ SỞ TIẾP NHẬN</span>
                            <div style={{ fontSize: '1rem', fontWeight: 500, color: '#0f172a' }}>
                                {registeredPatient.primaryFacilityName || 'Cơ sở chính'}
                            </div>
                        </div>
                    </div>

                    <div style={{ display: 'flex', gap: '12px', marginTop: '16px' }}>
                        <button
                            type="button"
                            onClick={() => window.print()}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '8px 16px',
                                backgroundColor: '#059669',
                                color: '#fff',
                                border: 'none',
                                borderRadius: '6px',
                                fontWeight: 500,
                                cursor: 'pointer'
                            }}
                        >
                            <Printer size={16} />
                            In phiếu tiếp nhận / Thẻ bệnh nhân
                        </button>
                    </div>
                </div>
            )}

            {/* Registration Form */}
            <form onSubmit={handleSubmit}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                    {/* Facility & Administrative Info */}
                    <div className="card" style={{ padding: '24px', borderRadius: '12px', border: '1px solid #e2e8f0', backgroundColor: '#fff' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '20px', borderBottom: '1px solid #f1f5f9', paddingBottom: '12px' }}>
                            <User size={20} color="#2563eb" />
                            <h2 style={{ fontSize: '1.15rem', fontWeight: 600, margin: 0, color: '#1e293b' }}>
                                Thông tin hành chính bệnh nhân
                            </h2>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '20px' }}>
                            {/* Primary Facility */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Cơ sở y tế tiếp nhận <span style={{ color: '#ef4444' }}>*</span>
                                </label>
                                <select
                                    className="form-select"
                                    value={primaryFacilityId || ''}
                                    onChange={(e) => setPrimaryFacilityId(e.target.value ? parseInt(e.target.value, 10) : undefined)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                    disabled={loadingFacilities}
                                >
                                    {facilities.map(f => (
                                        <option key={f.id} value={f.id}>
                                            [{f.code}] {f.name} - {f.city}
                                        </option>
                                    ))}
                                    {facilities.length === 0 && <option value="">Đang tải cơ sở...</option>}
                                </select>
                            </div>

                            {/* Full Name */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Họ và tên bệnh nhân <span style={{ color: '#ef4444' }}>*</span>
                                </label>
                                <input
                                    type="text"
                                    className="form-control"
                                    placeholder="Ví dụ: NGUYỄN VĂN A"
                                    value={fullName}
                                    onChange={(e) => setFullName(e.target.value.toUpperCase())}
                                    required
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            {/* Gender */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Giới tính <span style={{ color: '#ef4444' }}>*</span>
                                </label>
                                <select
                                    className="form-select"
                                    value={gender}
                                    onChange={(e) => setGender(e.target.value as GenderType)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                >
                                    <option value="Male">Nam</option>
                                    <option value="Female">Nữ</option>
                                    <option value="Other">Khác</option>
                                </select>
                            </div>

                            {/* Date of Birth */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Ngày sinh
                                </label>
                                <input
                                    type="date"
                                    className="form-control"
                                    value={dateOfBirth}
                                    onChange={(e) => setDateOfBirth(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            {/* Phone */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Số điện thoại
                                </label>
                                <input
                                    type="tel"
                                    className="form-control"
                                    placeholder="Ví dụ: 0912345678"
                                    value={phoneNumber}
                                    onChange={(e) => setPhoneNumber(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            {/* Email */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Email (Tùy chọn)
                                </label>
                                <input
                                    type="email"
                                    className="form-control"
                                    placeholder="benhnhan@example.com"
                                    value={email}
                                    onChange={(e) => setEmail(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            {/* CCCD / National ID */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Số CCCD / Hộ chiếu
                                </label>
                                <input
                                    type="text"
                                    className="form-control"
                                    placeholder="12 chữ số căn cước"
                                    value={nationalId}
                                    onChange={(e) => setNationalId(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            {/* BHYT Number */}
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Mã số thẻ BHYT
                                </label>
                                <input
                                    type="text"
                                    className="form-control"
                                    placeholder="Ví dụ: GD4010123456789"
                                    value={bhytNumber}
                                    onChange={(e) => setBhytNumber(e.target.value.toUpperCase())}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            {/* Address */}
                            <div style={{ gridColumn: '1 / -1' }}>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Địa chỉ thường trú / tạm trú
                                </label>
                                <input
                                    type="text"
                                    className="form-control"
                                    placeholder="Số nhà, đường, phường/xã, quận/huyện, tỉnh/thành phố"
                                    value={address}
                                    onChange={(e) => setAddress(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>
                        </div>
                    </div>

                    {/* Medical & Blood Information */}
                    <div className="card" style={{ padding: '24px', borderRadius: '12px', border: '1px solid #e2e8f0', backgroundColor: '#fff' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '20px', borderBottom: '1px solid #f1f5f9', paddingBottom: '12px' }}>
                            <HeartPulse size={20} color="#e11d48" />
                            <h2 style={{ fontSize: '1.15rem', fontWeight: 600, margin: 0, color: '#1e293b' }}>
                                Thông tin y tế & Nhóm máu
                            </h2>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '20px' }}>
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Hệ nhóm máu ABO
                                </label>
                                <select
                                    className="form-select"
                                    value={bloodType}
                                    onChange={(e) => setBloodType(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                >
                                    <option value="">Chưa xác định</option>
                                    <option value="A">Nhóm A</option>
                                    <option value="B">Nhóm B</option>
                                    <option value="AB">Nhóm AB</option>
                                    <option value="O">Nhóm O</option>
                                </select>
                            </div>

                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Yếu tố Rh
                                </label>
                                <select
                                    className="form-select"
                                    value={rhFactor}
                                    onChange={(e) => setRhFactor(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                >
                                    <option value="">Chưa xác định</option>
                                    <option value="+">Rh Dương (+)</option>
                                    <option value="-">Rh Âm (-)</option>
                                </select>
                            </div>
                        </div>
                    </div>

                    {/* Outpatient Clinic Department & Queue Check-in (Phase 1.2 Connected Outpatient Journey) */}
                    <div className="card" style={{ padding: '24px', borderRadius: '12px', border: '2px solid #0284c7', backgroundColor: '#f8fafc' }}>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px', borderBottom: '1px solid #e2e8f0', paddingBottom: '12px', flexWrap: 'wrap', gap: '8px' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                <Ticket size={22} color="#0284c7" />
                                <div>
                                    <h2 style={{ fontSize: '1.15rem', fontWeight: 700, margin: 0, color: '#0369a1' }}>
                                        Tiếp nhận khám ngay & Cấp số thứ tự (STT)
                                    </h2>
                                    <span style={{ fontSize: '0.85rem', color: '#64748b' }}>
                                        Phát số thứ tự tự động và đưa bệnh nhân vào hàng đợi phòng khám bác sĩ
                                    </span>
                                </div>
                            </div>
                            <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontWeight: 600, color: '#0f172a' }}>
                                <input
                                    type="checkbox"
                                    checked={issueQueueTicket}
                                    onChange={(e) => setIssueQueueTicket(e.target.checked)}
                                    style={{ width: '18px', height: '18px', accentColor: '#0284c7' }}
                                />
                                Cấp STT & Khám ngay
                            </label>
                        </div>

                        {issueQueueTicket && (
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '20px' }}>
                                <div>
                                    <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                        Khoa / Chuyên khoa khám <span style={{ color: '#ef4444' }}>*</span>
                                    </label>
                                    <select
                                        className="form-select"
                                        value={selectedDepartmentId || ''}
                                        onChange={(e) => setSelectedDepartmentId(e.target.value ? parseInt(e.target.value, 10) : undefined)}
                                        style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                        required
                                    >
                                        <option value="">-- Chọn chuyên khoa tiếp nhận --</option>
                                        {departments.map(d => (
                                            <option key={d.id} value={d.id}>
                                                [{d.code}] {d.name} {d.buildingName ? `(${d.buildingName})` : ''}
                                            </option>
                                        ))}
                                    </select>
                                </div>

                                <div>
                                    <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                        Phòng khám (Tùy chọn)
                                    </label>
                                    <select
                                        className="form-select"
                                        value={selectedRoomId || ''}
                                        onChange={(e) => setSelectedRoomId(e.target.value ? parseInt(e.target.value, 10) : undefined)}
                                        style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                    >
                                        <option value="">Phòng khám tự động / chung</option>
                                        {rooms.map(r => (
                                            <option key={r.id} value={r.id}>
                                                Phòng {r.roomNumber} - {r.name}
                                            </option>
                                        ))}
                                    </select>
                                </div>

                                <div>
                                    <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                        Mức độ ưu tiên khám
                                    </label>
                                    <select
                                        className="form-select"
                                        value={priority}
                                        onChange={(e) => setPriority(e.target.value as VisitPriority)}
                                        style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                    >
                                        <option value="Normal">Bình thường (Normal)</option>
                                        <option value="Priority">Ưu tiên (Người già / Trẻ em / Phụ nữ mang thai)</option>
                                        <option value="Urgent">Khẩn cấp (Cần xử lý sớm)</option>
                                        <option value="Emergency">Cấp cứu (Emergency)</option>
                                    </select>
                                </div>

                                <div style={{ gridColumn: '1 / -1' }}>
                                    <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                        Lý do khám / Triệu chứng ban đầu <span style={{ color: '#ef4444' }}>*</span>
                                    </label>
                                    <textarea
                                        className="form-control"
                                        rows={2}
                                        placeholder="Ví dụ: Đau đầu, sốt nhẹ 2 ngày, ho có đờm..."
                                        value={chiefComplaint}
                                        onChange={(e) => setChiefComplaint(e.target.value)}
                                        required={issueQueueTicket}
                                        style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                    />
                                </div>
                            </div>
                        )}
                    </div>

                    {/* Allergies Section */}
                    <div className="card" style={{ padding: '24px', borderRadius: '12px', border: '1px solid #e2e8f0', backgroundColor: '#fff' }}>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '20px', borderBottom: '1px solid #f1f5f9', paddingBottom: '12px' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                <ShieldAlert size={20} color="#ea580c" />
                                <h2 style={{ fontSize: '1.15rem', fontWeight: 600, margin: 0, color: '#1e293b' }}>
                                    Tiền sử dị ứng & Cảnh báo an toàn người bệnh
                                </h2>
                            </div>
                            <button
                                type="button"
                                onClick={handleAddAllergy}
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '6px',
                                    padding: '6px 14px',
                                    backgroundColor: '#fff7ed',
                                    color: '#c2410c',
                                    border: '1px solid #fdba74',
                                    borderRadius: '6px',
                                    fontWeight: 600,
                                    fontSize: '0.875rem',
                                    cursor: 'pointer'
                                }}
                            >
                                <Plus size={16} />
                                Thêm dị ứng
                            </button>
                        </div>

                        {allergies.length === 0 ? (
                            <div style={{ textAlign: 'center', padding: '24px', color: '#64748b', fontSize: '0.9rem' }}>
                                Chưa ghi nhận tiền sử dị ứng. Nhấn "Thêm dị ứng" nếu người bệnh có dị ứng thuốc, thức ăn hoặc dị nguyên khác.
                            </div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                {allergies.map((al, idx) => (
                                    <div
                                        key={idx}
                                        style={{
                                            display: 'grid',
                                            gridTemplateColumns: '180px 1fr 160px 1.5fr 40px',
                                            gap: '12px',
                                            alignItems: 'center',
                                            backgroundColor: '#f8fafc',
                                            padding: '12px',
                                            borderRadius: '8px',
                                            border: '1px solid #e2e8f0'
                                        }}
                                    >
                                        <div>
                                            <select
                                                className="form-select"
                                                value={al.allergenType}
                                                onChange={(e) => handleUpdateAllergy(idx, 'allergenType', parseInt(e.target.value, 10))}
                                                style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                            >
                                                <option value="1">Thuốc (Drug)</option>
                                                <option value="2">Thức ăn (Food)</option>
                                                <option value="3">Môi trường (Env)</option>
                                                <option value="4">Khác (Other)</option>
                                            </select>
                                        </div>
                                        <div>
                                            <input
                                                type="text"
                                                className="form-control"
                                                placeholder="Tên tác nhân (vd: Penicillin, Tôm...)"
                                                value={al.allergenName}
                                                onChange={(e) => handleUpdateAllergy(idx, 'allergenName', e.target.value)}
                                                style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                                required
                                            />
                                        </div>
                                        <div>
                                            <select
                                                className="form-select"
                                                value={al.severity}
                                                onChange={(e) => handleUpdateAllergy(idx, 'severity', parseInt(e.target.value, 10))}
                                                style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                            >
                                                <option value="1">Nhẹ (Mild)</option>
                                                <option value="2">Vừa (Moderate)</option>
                                                <option value="3">Nặng (Severe)</option>
                                                <option value="4">Sốc phản vệ</option>
                                            </select>
                                        </div>
                                        <div>
                                            <input
                                                type="text"
                                                className="form-control"
                                                placeholder="Mô tả phản ứng (vd: Phát ban, khó thở...)"
                                                value={al.reactionDescription}
                                                onChange={(e) => handleUpdateAllergy(idx, 'reactionDescription', e.target.value)}
                                                style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1' }}
                                            />
                                        </div>
                                        <div>
                                            <button
                                                type="button"
                                                onClick={() => handleRemoveAllergy(idx)}
                                                style={{
                                                    background: 'none',
                                                    border: 'none',
                                                    color: '#ef4444',
                                                    cursor: 'pointer',
                                                    padding: '6px',
                                                    borderRadius: '4px'
                                                }}
                                                title="Xóa dị ứng"
                                            >
                                                <Trash2 size={18} />
                                            </button>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>

                    {/* Emergency Contact */}
                    <div className="card" style={{ padding: '24px', borderRadius: '12px', border: '1px solid #e2e8f0', backgroundColor: '#fff' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '20px', borderBottom: '1px solid #f1f5f9', paddingBottom: '12px' }}>
                            <Phone size={20} color="#059669" />
                            <h2 style={{ fontSize: '1.15rem', fontWeight: 600, margin: 0, color: '#1e293b' }}>
                                Người liên hệ khẩn cấp
                            </h2>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '20px' }}>
                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Họ và tên người liên hệ
                                </label>
                                <input
                                    type="text"
                                    className="form-control"
                                    placeholder="Họ tên người thân"
                                    value={contactName}
                                    onChange={(e) => setContactName(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Mối quan hệ
                                </label>
                                <select
                                    className="form-select"
                                    value={contactRelationship}
                                    onChange={(e) => setContactRelationship(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                >
                                    <option value="Vợ/Chồng">Vợ / Chồng</option>
                                    <option value="Bố/Mẹ">Bố / Mẹ</option>
                                    <option value="Con">Con</option>
                                    <option value="Anh/Chị/Em">Anh / Chị / Em</option>
                                    <option value="Người giám hộ">Người giám hộ</option>
                                    <option value="Người thân">Người thân khác</option>
                                </select>
                            </div>

                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Số điện thoại người liên hệ
                                </label>
                                <input
                                    type="tel"
                                    className="form-control"
                                    placeholder="Số điện thoại liên lạc"
                                    value={contactPhone}
                                    onChange={(e) => setContactPhone(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>

                            <div>
                                <label style={{ display: 'block', fontSize: '0.875rem', fontWeight: 600, marginBottom: '6px', color: '#334155' }}>
                                    Địa chỉ người liên hệ
                                </label>
                                <input
                                    type="text"
                                    className="form-control"
                                    placeholder="Địa chỉ nếu khác người bệnh"
                                    value={contactAddress}
                                    onChange={(e) => setContactAddress(e.target.value)}
                                    style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                                />
                            </div>
                        </div>
                    </div>

                    {/* Action Bar */}
                    <div style={{
                        display: 'flex',
                        justifyContent: 'flex-end',
                        gap: '12px',
                        padding: '16px 24px',
                        backgroundColor: '#fff',
                        borderRadius: '12px',
                        border: '1px solid #e2e8f0'
                    }}>
                        <button
                            type="button"
                            onClick={resetForm}
                            disabled={submitting}
                            style={{
                                padding: '12px 24px',
                                backgroundColor: '#f1f5f9',
                                color: '#475569',
                                border: '1px solid #cbd5e1',
                                borderRadius: '8px',
                                fontWeight: 500,
                                cursor: submitting ? 'not-allowed' : 'pointer'
                            }}
                        >
                            Làm mới biểu mẫu
                        </button>
                        <button
                            type="submit"
                            disabled={submitting}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '12px 28px',
                                backgroundColor: issueQueueTicket ? '#059669' : '#2563eb',
                                color: '#fff',
                                border: 'none',
                                borderRadius: '8px',
                                fontWeight: 600,
                                cursor: submitting ? 'not-allowed' : 'pointer',
                                opacity: submitting ? 0.7 : 1
                            }}
                        >
                            {issueQueueTicket ? <Ticket size={18} /> : <UserPlus size={18} />}
                            {submitting 
                                ? 'Đang tiếp nhận & cấp mã...' 
                                : (issueQueueTicket ? 'Tiếp nhận phòng khám & Cấp STT' : 'Xác nhận tiếp nhận & Cấp MRN')}
                        </button>
                    </div>
                </div>
            </form>

            {/* Check-In Ticket Printable Modal */}
            <CheckInTicketModal
                isOpen={ticketModalOpen}
                ticket={currentTicket}
                onClose={() => setTicketModalOpen(false)}
            />

            {/* MPI Lookup Modal */}
            <MpiPatientSearchModal
                isOpen={searchModalOpen}
                onClose={() => setSearchModalOpen(false)}
                onSelectPatient={handleSelectExistingPatient}
            />
        </div>
    );
};
