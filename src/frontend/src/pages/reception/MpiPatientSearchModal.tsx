import React, { useState, useEffect } from 'react';
import { Search, AlertTriangle, UserCheck, ShieldAlert, Phone, CreditCard } from 'lucide-react';
import { Button, Input, List, Modal, Select } from 'antd';
import { EmptyState, LoadingState } from '../../components/common';
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
        <Modal open={isOpen} onCancel={onClose} width={900} className={styles.patientSearchModal}
            title={<div className={styles.modalHeading}><div className={styles.searchIcon}><Search size={20} /></div><div><h3 className={styles.modalTitle}>Tra cứu hồ sơ bệnh nhân (MPI)</h3><p className={styles.modalDescription}>Tìm kiếm bệnh nhân theo Mã bệnh án (MRN), Họ tên, SĐT, CCCD hoặc Thẻ BHYT</p></div></div>}
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
                        'Nhập từ khóa tìm kiếm (Tên, MRN, SĐT, CCCD, BHYT)...'
                    }
                    value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} autoFocus />
                <Button htmlType="submit" type="primary" disabled={loading || !searchTerm.trim()}><Search size={18} />{loading ? 'Đang tìm...' : 'Tra cứu'}</Button>
            </form>
            <div className={styles.patientSearchResults}>
                {loading ? <LoadingState message="Đang đối soát dữ liệu bệnh nhân toàn viện..." height="280px" /> : !searched ? (
                    <EmptyState icon={<Search size={44} strokeWidth={1.5} className={styles.emptySearchIcon} />} title="Chưa thực hiện tra cứu" description="Nhập mã bệnh án, CCCD hoặc thông tin bệnh nhân để kiểm tra danh mục MPI" />
                ) : patients.length === 0 ? (
                    <EmptyState icon={<AlertTriangle size={44} strokeWidth={1.5} className={styles.noResultsIcon} />} title="Không tìm thấy bệnh nhân nào" description={`Không có hồ sơ nào trùng khớp với từ khóa "${searchTerm}". Bạn có thể thực hiện tiếp nhận bệnh nhân mới.`} />
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
