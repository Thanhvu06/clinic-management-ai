import { Button, Input, Select, Modal } from 'antd';
import { PageHeader, DataTable, StatusBadge, LoadingState, EmptyState, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './ReceptionPackageRegistrations.module.css';
import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Package, CheckCircle, RefreshCw, AlertTriangle } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
import { formatVndCurrency, formatDisplayDate } from '../../utils/formatters';

interface PackageRegistrationItem {
    id: number;
    registrationCode: string;
    patientId: number;
    patientName: string;
    healthPackageId: number;
    healthPackageName?: string;
    packageName?: string;
    healthPackageCode?: string;
    packageCode?: string;
    healthPackagePrice?: number;
    packagePrice?: number;
    preferredDate: string;
    contactPhone: string;
    note?: string;
    notes?: string;
    adminNotes?: string;
    cancellationReason?: string;
    status: 'Pending' | 'Confirmed' | 'Completed' | 'Cancelled' | string;
    createdAt: string;
}

export const ReceptionPackageRegistrations: React.FC = () => {
    const [registrations, setRegistrations] = useState<PackageRegistrationItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    const [actionLoading, setActionLoading] = useState(false);
    const [selectedItem, setSelectedItem] = useState<PackageRegistrationItem | null>(null);
    const [actionType, setActionType] = useState<'confirm' | 'cancel' | null>(null);
    const [actionNote, setActionNote] = useState('');
    const [actionError, setActionError] = useState<string | null>(null);

    const { showToast } = useDialog();

    const fetchRegistrations = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/health-package-registrations?${params.toString()}`);
            if (res.success && res.data) {
                setRegistrations(res.data.items || []);
                setTotalItems(res.data.totalItems || 0);
            }
        } catch (error) {
            console.error("Lỗi tải danh sách đăng ký gói khám", error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRegistrations();
    }, [page, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchRegistrations();
    };

    const handleConfirmAction = async () => {
        if (!selectedItem || !actionType || actionLoading) return;
        setActionLoading(true);
        setActionError(null);

        try {
            let res: any;
            if (actionType === 'confirm') {
                res = await axiosClient.post(`/reception/health-package-registrations/${selectedItem.id}/confirm`, {
                    notes: actionNote.trim()
                });
            } else {
                res = await axiosClient.post(`/reception/health-package-registrations/${selectedItem.id}/cancel`, {
                    cancellationReason: actionNote.trim() || 'Hủy theo yêu cầu của phòng khám/khách hàng'
                });
            }

            if (res.success) {
                showToast(actionType === 'confirm' ? 'Đã xác nhận đăng ký gói khám!' : 'Đã hủy đăng ký gói khám.', 'success');
                setSelectedItem(null);
                setActionType(null);
                setActionNote('');
                fetchRegistrations();
            } else {
                setActionError(res.message || 'Thao tác không thành công.');
            }
        } catch (err: any) {
            setActionError(err?.response?.data?.message || 'Có lỗi xảy ra trong quá trình xử lý.');
        } finally {
            setActionLoading(false);
        }
    };

    const getStatusBadge = (status: string) => <StatusBadge status={status} label={({Pending:'Chờ xác nhận',Confirmed:'Đã xác nhận',Completed:'Đã hoàn tất',Cancelled:'Đã hủy'} as Record<string,string>)[status] || '—'} />;
    const columns: DataTableColumn<PackageRegistrationItem>[] = [
        {header:'Mã đăng ký',accessor:reg => <><div className={styles.registrationCode}>{reg.registrationCode}</div><div className={styles.secondaryText}>{formatDisplayDate(reg.createdAt)}</div></>},
        {header:'Bệnh nhân',accessor:reg => <><div className={styles.primaryText}>{reg.patientName}</div><div className={styles.secondaryText}>{reg.contactPhone}</div></>},
        {header:'Gói khám',accessor:reg => <><div className={styles.primaryText}>{reg.healthPackageName || reg.packageName}</div><div className={styles.secondaryText}>Mã: {reg.healthPackageCode || reg.packageCode}</div>{(reg.note || reg.notes) && <div className={styles.patientNote}>"{reg.note || reg.notes}"</div>}{reg.adminNotes && <div className={styles.staffNote}>Lễ tân: {reg.adminNotes}</div>}{reg.cancellationReason && <div className={styles.cancellationReason}>Lý do hủy: {reg.cancellationReason}</div>}</>},
        {header:'Ngày mong muốn',accessor:'preferredDate'},
        {header:'Chi phí',accessor:reg => <span className={styles.price}>{formatVndCurrency(reg.healthPackagePrice ?? reg.packagePrice ?? 0)}</span>},
        {header:'Trạng thái',accessor:reg => getStatusBadge(reg.status)},
        {header:'Thao tác',align:'right',className:styles.actionsCell,accessor:reg => reg.status === 'Pending' ? <><Button type="primary" onClick={() => { setSelectedItem(reg); setActionType('confirm'); setActionNote(''); setActionError(null); }}>Xác nhận</Button><Button danger onClick={() => { setSelectedItem(reg); setActionType('cancel'); setActionNote(''); setActionError(null); }}>Hủy</Button></> : <span className={styles.secondaryText}>—</span>},
    ];
    return (
        <div className={styles.page}>
            <PageHeader title="Quản lý Đăng ký Gói khám Sức khỏe" subtitle="Xác nhận và hỗ trợ bệnh nhân đã đăng ký các gói khám định kỳ trực tuyến." badge={<Package size={24} className={styles.primaryIcon} />} actions={<Button onClick={fetchRegistrations} title="Làm mới danh sách" icon={<RefreshCw size={18} />}>Làm mới</Button>} />
            <div className={styles.filters}><form onSubmit={handleSearchSubmit} className={styles.searchForm}><Input className={styles.searchInput} prefix={<Search size={18} />} placeholder="Tìm theo mã đăng ký, tên bệnh nhân, SĐT..." value={search} onChange={e => setSearch(e.target.value)} /><Button htmlType="submit">Tìm kiếm</Button></form><Select className={styles.statusFilter} value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[{value:'',label:'Tất cả trạng thái'},{value:'Pending',label:'Chờ xác nhận'},{value:'Confirmed',label:'Đã xác nhận'},{value:'Completed',label:'Đã hoàn tất'},{value:'Cancelled',label:'Đã hủy'}]} /></div>
            {loading ? <LoadingState message="Đang tải danh sách đăng ký gói khám..." /> : registrations.length === 0 ? <EmptyState icon={<Package size={48} />} title="Không có dữ liệu đăng ký gói khám nào phù hợp." /> : <div className={styles.tableScroll}><DataTable columns={columns} data={registrations} keyExtractor={reg => reg.id} /></div>}
            {totalItems > 10 && <div className={styles.pagination}><span className={styles.secondaryText}>Hiển thị {registrations.length} / {totalItems} bản ghi</span><div className={styles.paginationActions}><Button disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Trang trước</Button><Button disabled={page * 10 >= totalItems} onClick={() => setPage(p => p + 1)}>Trang sau</Button></div></div>}
            {selectedItem && actionType && <Modal open width={480} className={styles.actionModal} closable={false} mask={{closable:false}} keyboard={false} onCancel={() => { if (!actionLoading) { setSelectedItem(null); setActionType(null); } }} title={<span className={actionType === 'confirm' ? styles.confirmTitle : styles.cancelTitle}>{actionType === 'confirm' ? <CheckCircle size={24} /> : <AlertTriangle size={24} />}{actionType === 'confirm' ? 'Xác nhận đăng ký gói khám' : 'Hủy đăng ký gói khám'}</span>} footer={<><Button onClick={() => { setSelectedItem(null); setActionType(null); }} disabled={actionLoading}>Đóng</Button><Button type="primary" danger={actionType === 'cancel'} onClick={handleConfirmAction} disabled={actionLoading}>{actionLoading ? 'Đang xử lý...' : (actionType === 'confirm' ? 'Xác nhận đăng ký' : 'Xác nhận hủy')}</Button></>}>
                <p className={styles.actionDescription}>{actionType === 'confirm' ? <>Xác nhận thông tin gói khám <strong>{selectedItem.packageName}</strong> cho người bệnh <strong>{selectedItem.patientName}</strong> vào ngày <strong>{selectedItem.preferredDate}</strong>?</> : <>Bạn có chắc muốn hủy đăng ký gói khám <strong>{selectedItem.registrationCode}</strong> của người bệnh <strong>{selectedItem.patientName}</strong>?</>}</p>
                {actionError && <InlineError title="" message={actionError} />}
                <label className={styles.noteLabel}>{actionType === 'confirm' ? 'Ghi chú lễ tân (Không bắt buộc)' : 'Lý do hủy đăng ký (*)'}</label><Input.TextArea rows={3} value={actionNote} onChange={e => setActionNote(e.target.value)} placeholder={actionType === 'confirm' ? 'Nhập ghi chú tiếp đón nếu có...' : 'Nhập lý do hủy...'} />
            </Modal>}
        </div>
    );
};
