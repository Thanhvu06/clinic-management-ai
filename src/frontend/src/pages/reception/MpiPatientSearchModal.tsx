import React, { useState, useEffect, useRef } from 'react';
import { Search, AlertTriangle, UserCheck, ShieldAlert, Phone, CreditCard } from 'lucide-react';
import { Button, Input, List, Modal, Select } from 'antd';
import { EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './MpiPatientSearchModal.module.css';
import { mpiApi, type MpiPatientDto, type GenderType } from '../../api/mpiApi';

export const formatGender = (gender?: GenderType | number | null, genderName?: string | null): string => {
    if (genderName) return genderName;
    if (gender === 'Male' || gender === 0) return 'Nam';
    if (gender === 'Female' || gender === 1) return 'Nữ';
    if (gender === 'Other' || gender === 2) return 'Khác';
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
    const [searchedTerm, setSearchedTerm] = useState('');
    const [error, setError] = useState<string | null>(null);
    const lastSearch = useRef<Parameters<typeof mpiApi.searchPatients>[0] | null>(null);
    const requestId = useRef(0);

    useEffect(() => {
        ++requestId.current;
        setLoading(false);
        setError(null);
        lastSearch.current = null;
        if (isOpen) {
            setSearchTerm('');
            setPatients([]);
            setSearched(false);
            setPage(1);
        }
    }, [isOpen]);

    if (!isOpen) return null;

    const performSearch = async (params: Parameters<typeof mpiApi.searchPatients>[0]) => {
        const currentRequest = ++requestId.current;
        lastSearch.current = params;
        setLoading(true);
        setError(null);
        setSearched(true);
        setSearchedTerm(params?.searchTerm ?? params?.medicalRecordNumber ?? params?.phoneNumber ?? params?.nationalId ?? params?.bhytNumber ?? '');
        try {
            const res = await mpiApi.searchPatients(params);
            if (currentRequest !== requestId.current) return;
            if (!res.success || !res.data) throw new Error('MPI lookup failed');
            setPatients(res.data.items);
            setTotalItems(res.data.totalItems);
        } catch (err) {
            if (currentRequest !== requestId.current) return;
            console.error('Lỗi khi tra cứu bệnh nhân MPI:', err);
            setError('Không thể tra cứu hồ sơ. Vui lòng thử lại trước khi tạo hồ sơ mới.');
        } finally {
            if (currentRequest === requestId.current) setLoading(false);
        }
    };

    const handleSearch = (e?: React.FormEvent) => {
        e?.preventDefault();
        const term = searchTerm.trim();
        if (!term) return;
        const params: Parameters<typeof mpiApi.searchPatients>[0] = { page, pageSize: 10 };
        if (searchType === 'all') params.searchTerm = term;
        else if (searchType === 'mrn') params.medicalRecordNumber = term;
        else if (searchType === 'phone') params.phoneNumber = term;
        else if (searchType === 'nationalId') params.nationalId = term;
        else params.bhytNumber = term;
        void performSearch(params);
    };

    return (
        <Modal open={isOpen} onCancel={onClose} width={900} className={styles.patientSearchModal}
            title={<div className={styles.modalHeading}><div className={styles.searchIcon}><Search size={20} /></div><div><h3 className={styles.modalTitle}>Tra cứu hồ sơ bệnh nhân (MPI)</h3><p className={styles.modalDescription}>Tìm kiếm bệnh nhân theo mã bệnh án (MRN), họ tên, SĐT, CCCD hoặc thẻ BHYT</p></div></div>}
            footer={<Button htmlType="button" onClick={onClose}>Đóng</Button>}>
            <form onSubmit={handleSearch} className={styles.patientSearchForm}>
                <Select className={styles.searchType} value={searchType} onChange={(value) => setSearchType(value)}
                    options={[
                        { value: 'all', label: 'Tất cả thông tin' },
                        { value: 'mrn', label: 'Mã bệnh án (MRN)' },
                        { value: 'phone', label: 'Số điện thoại' },
                        { value: 'nationalId', label: 'Số CCCD/CMND' },
                        { value: 'bhyt', label: 'Mã thẻ BHYT' }
                    ]} />
                <Input type="text" className={styles.searchInput}
                    placeholder={
                        searchType === 'mrn' ? 'Nhập mã BN (vd: BN-2026-000001)...' :
                        searchType === 'phone' ? 'Nhập số điện thoại (vd: 0912345678)...' :
                        searchType === 'nationalId' ? 'Nhập số CCCD 12 số...' :
                        searchType === 'bhyt' ? 'Nhập mã BHYT 15 ký tự...' :
                        'Nhập từ khóa tìm kiếm (tên, MRN, SĐT, CCCD, BHYT)...'
                    }
                    value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} autoFocus />
                <Button htmlType="submit" type="primary" disabled={loading || !searchTerm.trim()}><Search size={18} />{loading ? 'Đang tìm...' : 'Tra cứu'}</Button>
            </form>
            <div className={styles.patientSearchResults}>
                {loading ? <LoadingState message="Đang đối soát dữ liệu bệnh nhân toàn viện..." height="280px" /> : error ? <InlineError message={error} onRetry={() => { if (lastSearch.current) void performSearch(lastSearch.current); }} /> : !searched ? (
                    <EmptyState icon={<Search size={44} strokeWidth={1.5} className={styles.emptySearchIcon} />} title="Chưa thực hiện tra cứu" description="Nhập mã bệnh án, CCCD hoặc thông tin bệnh nhân để kiểm tra danh mục MPI" />
                ) : patients.length === 0 ? (
                    <EmptyState icon={<AlertTriangle size={44} strokeWidth={1.5} className={styles.noResultsIcon} />} title="Không tìm thấy bệnh nhân nào" description={`Không có hồ sơ nào trùng khớp với từ khóa "${searchedTerm}". Bạn có thể thực hiện tiếp nhận bệnh nhân mới.`} />
                ) : <>
                    <div className={styles.resultCount}>Tìm thấy <strong>{totalItems}</strong> hồ sơ trùng khớp:</div>
                    <List dataSource={patients} split={false} renderItem={p => (
                        <List.Item key={p.id} className={styles.patientSearchResult}>
                            <div className={styles.patientDetails}>
                                <div className={styles.patientIdentity}>
                                    <span className={styles.medicalRecordNumber}>{p.medicalRecordNumber}</span>
                                    <span className={styles.patientName}>{p.fullName}</span>
                                    <span className={styles.patientDemographics}>({formatGender(p.gender, p.genderName)}{p.age ? ` - ${p.age} tuổi` : ''})</span>
                                    {p.bloodType && <span className={styles.bloodType}>Nhóm máu: {p.bloodType}{p.rhFactor || ''}</span>}
                                </div>
                                <div className={styles.patientContacts}>
                                    {p.phoneNumber && <span className={styles.contactItem}><Phone size={13} /> {p.phoneNumber}</span>}
                                    {p.nationalId && <span className={styles.contactItem}><CreditCard size={13} /> CCCD: {p.nationalId}</span>}
                                    {p.bhytNumber && <span className={styles.contactItem}>BHYT: {p.bhytNumber}</span>}
                                    {p.primaryFacilityName && <span className={styles.patientFacility}>Cơ sở: {p.primaryFacilityName}</span>}
                                </div>
                                {p.allergies && p.allergies.length > 0 && <div className={styles.patientAllergies}>
                                    <ShieldAlert size={14} className={styles.allergyIcon} /><span className={styles.allergyLabel}>Dị ứng ({p.allergies.length}):</span>
                                    <div className={styles.allergyList}>{p.allergies.map(al => <span key={al.id} className={`${styles.allergyTag} ${al.severity >= 3 ? styles.severeAllergy : styles.moderateAllergy}`}>{al.allergenName} ({al.severityName})</span>)}</div>
                                </div>}
                            </div>
                            <Button htmlType="button" type="primary" className={styles.selectPatientButton} onClick={() => { onSelectPatient(p); onClose(); }}><UserCheck size={16} /> Chọn hồ sơ</Button>
                        </List.Item>
                    )} />
                </>}
            </div>
        </Modal>
    );
};
