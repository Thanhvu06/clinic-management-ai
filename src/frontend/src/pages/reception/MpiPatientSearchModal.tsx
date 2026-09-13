import React, { useState, useEffect } from 'react';
import { Search, X, AlertTriangle, UserCheck, ShieldAlert, Phone, CreditCard } from 'lucide-react';
import { mpiApi, type MpiPatientDto } from '../../api/mpiApi';

export const formatGender = (gender?: string | number | null, genderName?: string | null): string => {
    if (genderName) return genderName;
    if (gender === 'Male' || gender === 1 || gender === 0) return 'Nam';
    if (gender === 'Female' || gender === 2) return 'Nữ';
    if (gender === 'Other' || gender === 3) return 'Khác';
    if (typeof gender === 'string' && gender.trim()) return gender;
    return 'Chưa rõ';
};

interface MpiPatientSearchModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSelectPatient: (patient: MpiPatientDto) => void;
}

export const MpiPatientSearchModal: React.FC<MpiPatientSearchModalProps> = ({
    isOpen,
    onClose,
    onSelectPatient
}) => {
    const [searchTerm, setSearchTerm] = useState('');
    const [searchType, setSearchType] = useState<'all' | 'mrn' | 'phone' | 'nationalId' | 'bhyt'>('all');
    const [loading, setLoading] = useState(false);
    const [patients, setPatients] = useState<MpiPatientDto[]>([]);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [searched, setSearched] = useState(false);

    useEffect(() => {
        if (isOpen) {
            setSearchTerm('');
            setPatients([]);
            setSearched(false);
            setPage(1);
        }
    }, [isOpen]);

    if (!isOpen) return null;

    const handleSearch = async (e?: React.FormEvent) => {
        if (e) e.preventDefault();
        if (!searchTerm.trim()) {
            return;
        }

        setLoading(true);
        setSearched(true);
        try {
            const params: any = { page, pageSize: 10 };
            if (searchType === 'all') {
                params.searchTerm = searchTerm.trim();
            } else if (searchType === 'mrn') {
                params.medicalRecordNumber = searchTerm.trim();
            } else if (searchType === 'phone') {
                params.phoneNumber = searchTerm.trim();
            } else if (searchType === 'nationalId') {
                params.nationalId = searchTerm.trim();
            } else if (searchType === 'bhyt') {
                params.bhytNumber = searchTerm.trim();
            }

            const res = await mpiApi.searchPatients(params);
            if (res.success && res.data) {
                setPatients(res.data.items);
                setTotalItems(res.data.totalItems);
            } else {
                setPatients([]);
                setTotalItems(0);
            }
        } catch (err) {
            console.error('Lỗi khi tra cứu bệnh nhân MPI:', err);
            setPatients([]);
            setTotalItems(0);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div style={{
            position: 'fixed',
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: 'rgba(0, 0, 0, 0.5)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1100,
            padding: '20px'
        }}>
            <div style={{
                backgroundColor: '#fff',
                borderRadius: '12px',
                width: '100%',
                maxWidth: '900px',
                maxHeight: '90vh',
                display: 'flex',
                flexDirection: 'column',
                boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                overflow: 'hidden'
            }}>
                {/* Header */}
                <div style={{
                    padding: '16px 24px',
                    borderBottom: '1px solid var(--c-border, #e2e8f0)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    backgroundColor: '#f8fafc'
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div style={{
                            padding: '8px',
                            backgroundColor: '#e0f2fe',
                            color: '#0369a1',
                            borderRadius: '8px',
                            display: 'flex'
                        }}>
                            <Search size={20} />
                        </div>
                        <div>
                            <h3 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 600, color: '#0f172a' }}>
                                Tra cứu hồ sơ bệnh nhân (MPI)
                            </h3>
                            <p style={{ margin: 0, fontSize: '0.85rem', color: '#64748b' }}>
                                Tìm kiếm bệnh nhân theo Mã bệnh án (MRN), Họ tên, SĐT, CCCD hoặc Thẻ BHYT
                            </p>
                        </div>
                    </div>
                    <button
                        onClick={onClose}
                        style={{
                            background: 'none',
                            border: 'none',
                            cursor: 'pointer',
                            color: '#64748b',
                            padding: '4px',
                            borderRadius: '6px'
                        }}
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Search Form */}
                <div style={{ padding: '20px 24px', borderBottom: '1px solid var(--c-border, #e2e8f0)' }}>
                    <form onSubmit={handleSearch} style={{ display: 'flex', gap: '12px', alignItems: 'center' }}>
                        <div style={{ width: '180px' }}>
                            <select
                                className="form-select"
                                value={searchType}
                                onChange={(e) => setSearchType(e.target.value as any)}
                                style={{ width: '100%', padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                            >
                                <option value="all">Tất cả thông tin</option>
                                <option value="mrn">Mã bệnh án (MRN)</option>
                                <option value="phone">Số điện thoại</option>
                                <option value="nationalId">Số CCCD/CMND</option>
                                <option value="bhyt">Mã thẻ BHYT</option>
                            </select>
                        </div>
                        <div style={{ flex: 1, position: 'relative' }}>
                            <input
                                type="text"
                                className="form-control"
                                placeholder={
                                    searchType === 'mrn' ? 'Nhập mã BN (vd: BN-2026-000001)...' :
                                    searchType === 'phone' ? 'Nhập số điện thoại (vd: 0912345678)...' :
                                    searchType === 'nationalId' ? 'Nhập số CCCD 12 số...' :
                                    searchType === 'bhyt' ? 'Nhập mã BHYT 15 ký tự...' :
                                    'Nhập từ khóa tìm kiếm (Tên, MRN, SĐT, CCCD, BHYT)...'
                                }
                                value={searchTerm}
                                onChange={(e) => setSearchTerm(e.target.value)}
                                autoFocus
                                style={{ width: '100%', padding: '10px 14px', borderRadius: '8px', border: '1px solid #cbd5e1' }}
                            />
                        </div>
                        <button
                            type="submit"
                            disabled={loading || !searchTerm.trim()}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '10px 20px',
                                backgroundColor: '#2563eb',
                                color: '#fff',
                                border: 'none',
                                borderRadius: '8px',
                                fontWeight: 500,
                                cursor: loading || !searchTerm.trim() ? 'not-allowed' : 'pointer',
                                opacity: loading || !searchTerm.trim() ? 0.6 : 1
                            }}
                        >
                            <Search size={18} />
                            {loading ? 'Đang tìm...' : 'Tra cứu'}
                        </button>
                    </form>
                </div>

                {/* Results List */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '16px 24px', minHeight: '280px' }}>
                    {loading ? (
                        <div style={{ textAlign: 'center', padding: '60px 20px', color: '#64748b' }}>
                            <div className="spinner" style={{ margin: '0 auto 12px' }} />
                            <div>Đang đối soát dữ liệu bệnh nhân toàn viện...</div>
                        </div>
                    ) : !searched ? (
                        <div style={{ textAlign: 'center', padding: '60px 20px', color: '#64748b' }}>
                            <Search size={44} style={{ margin: '0 auto 12px', strokeWidth: 1.5, color: '#94a3b8' }} />
                            <div style={{ fontWeight: 500, fontSize: '1rem' }}>Chưa thực hiện tra cứu</div>
                            <div style={{ fontSize: '0.875rem', marginTop: '4px' }}>
                                Nhập mã bệnh án, CCCD hoặc thông tin bệnh nhân để kiểm tra danh mục MPI
                            </div>
                        </div>
                    ) : patients.length === 0 ? (
                        <div style={{ textAlign: 'center', padding: '60px 20px', color: '#64748b' }}>
                            <AlertTriangle size={44} style={{ margin: '0 auto 12px', strokeWidth: 1.5, color: '#f59e0b' }} />
                            <div style={{ fontWeight: 600, fontSize: '1.05rem', color: '#1e293b' }}>
                                Không tìm thấy bệnh nhân nào
                            </div>
                            <div style={{ fontSize: '0.875rem', marginTop: '4px', maxWidth: '400px', margin: '4px auto 0' }}>
                                Không có hồ sơ nào trùng khớp với từ khóa "{searchTerm}". Bạn có thể thực hiện tiếp nhận bệnh nhân mới.
                            </div>
                        </div>
                    ) : (
                        <div>
                            <div style={{ marginBottom: '12px', fontSize: '0.875rem', color: '#64748b' }}>
                                Tìm thấy <strong>{totalItems}</strong> hồ sơ trùng khớp:
                            </div>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                                {patients.map(p => (
                                    <div
                                        key={p.id}
                                        style={{
                                            border: '1px solid #e2e8f0',
                                            borderRadius: '8px',
                                            padding: '14px 16px',
                                            display: 'flex',
                                            alignItems: 'center',
                                            justifyContent: 'space-between',
                                            backgroundColor: '#fff',
                                            transition: 'border-color 0.15s, box-shadow 0.15s'
                                        }}
                                    >
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                                <span style={{
                                                    backgroundColor: '#0284c7',
                                                    color: '#fff',
                                                    fontWeight: 700,
                                                    fontSize: '0.8rem',
                                                    padding: '2px 8px',
                                                    borderRadius: '4px',
                                                    letterSpacing: '0.5px'
                                                }}>
                                                    {p.medicalRecordNumber}
                                                </span>
                                                <span style={{ fontWeight: 600, fontSize: '1.05rem', color: '#0f172a' }}>
                                                    {p.fullName}
                                                </span>
                                                <span style={{ fontSize: '0.85rem', color: '#64748b' }}>
                                                    ({formatGender(p.gender, p.genderName)}{p.age ? ` - ${p.age} tuổi` : ''})
                                                </span>
                                                {p.bloodType && (
                                                    <span style={{
                                                        backgroundColor: '#fee2e2',
                                                        color: '#dc2626',
                                                        fontWeight: 600,
                                                        fontSize: '0.75rem',
                                                        padding: '1px 6px',
                                                        borderRadius: '4px'
                                                    }}>
                                                        Nhóm máu: {p.bloodType}{p.rhFactor || ''}
                                                    </span>
                                                )}
                                            </div>

                                            <div style={{ display: 'flex', alignItems: 'center', gap: '16px', fontSize: '0.85rem', color: '#475569', marginTop: '2px' }}>
                                                {p.phoneNumber && (
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                        <Phone size={13} color="#64748b" /> {p.phoneNumber}
                                                    </span>
                                                )}
                                                {p.nationalId && (
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                        <CreditCard size={13} color="#64748b" /> CCCD: {p.nationalId}
                                                    </span>
                                                )}
                                                {p.bhytNumber && (
                                                    <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                        BHYT: {p.bhytNumber}
                                                    </span>
                                                )}
                                                {p.primaryFacilityName && (
                                                    <span style={{ color: '#0284c7' }}>
                                                        Cơ sở: {p.primaryFacilityName}
                                                    </span>
                                                )}
                                            </div>

                                            {p.allergies && p.allergies.length > 0 && (
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', marginTop: '4px' }}>
                                                    <ShieldAlert size={14} color="#dc2626" />
                                                    <span style={{ fontSize: '0.8rem', fontWeight: 600, color: '#dc2626' }}>
                                                        Dị ứng ({p.allergies.length}):
                                                    </span>
                                                    <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                                                        {p.allergies.map(al => (
                                                            <span
                                                                key={al.id}
                                                                style={{
                                                                    fontSize: '0.75rem',
                                                                    backgroundColor: al.severity >= 3 ? '#fee2e2' : '#fef3c7',
                                                                    color: al.severity >= 3 ? '#b91c1c' : '#92400e',
                                                                    padding: '1px 6px',
                                                                    borderRadius: '4px'
                                                                }}
                                                            >
                                                                {al.allergenName} ({al.severityName})
                                                            </span>
                                                        ))}
                                                    </div>
                                                </div>
                                            )}
                                        </div>

                                        <button
                                            onClick={() => {
                                                onSelectPatient(p);
                                                onClose();
                                            }}
                                            style={{
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: '6px',
                                                padding: '8px 16px',
                                                backgroundColor: '#10b981',
                                                color: '#fff',
                                                border: 'none',
                                                borderRadius: '6px',
                                                fontWeight: 500,
                                                cursor: 'pointer',
                                                whiteSpace: 'nowrap'
                                            }}
                                        >
                                            <UserCheck size={16} />
                                            Chọn hồ sơ
                                        </button>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}
                </div>

                {/* Footer */}
                <div style={{
                    padding: '14px 24px',
                    borderTop: '1px solid var(--c-border, #e2e8f0)',
                    backgroundColor: '#f8fafc',
                    display: 'flex',
                    justifyContent: 'flex-end'
                }}>
                    <button
                        onClick={onClose}
                        style={{
                            padding: '8px 18px',
                            backgroundColor: '#e2e8f0',
                            color: '#334155',
                            border: 'none',
                            borderRadius: '6px',
                            fontWeight: 500,
                            cursor: 'pointer'
                        }}
                    >
                        Đóng
                    </button>
                </div>
            </div>
        </div>
    );
};
