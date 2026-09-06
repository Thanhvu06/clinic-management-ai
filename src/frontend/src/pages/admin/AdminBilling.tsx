import React, { useState, useEffect } from 'react';
import {
    TrendingUp,
    DollarSign,
    Calendar,
    Stethoscope,
    Edit,
    RefreshCw,
    X,
    CheckCircle,
    Clock,
    XCircle,
    Layers,
} from 'lucide-react';
import { billingApi } from '../../api/billingApi';
import type {
    RevenueReportDto,
    SpecialtyFeeDto,
} from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import styles from './AdminBilling.module.css';

export const AdminBilling: React.FC = () => {
    const { showAlert } = useDialog();

    // Active View Tab: 'revenue' or 'fees'
    const [activeTab, setActiveTab] = useState<'revenue' | 'fees'>('revenue');

    // Revenue state
    const [revenueReport, setRevenueReport] = useState<RevenueReportDto | null>(null);
    const [revLoading, setRevLoading] = useState<boolean>(true);

    // Date range for report (default: past 30 days to today)
    const todayStr = new Date().toISOString().split('T')[0];
    const thirtyDaysAgoStr = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().split('T')[0];
    const [fromDate, setFromDate] = useState<string>(thirtyDaysAgoStr);
    const [toDate, setToDate] = useState<string>(todayStr);

    // Specialty fees state
    const [specialtyFees, setSpecialtyFees] = useState<SpecialtyFeeDto[]>([]);
    const [feesLoading, setFeesLoading] = useState<boolean>(true);

    // Edit Fee Modal
    const [editModalOpen, setEditModalOpen] = useState<boolean>(false);
    const [selectedSpecialty, setSelectedSpecialty] = useState<SpecialtyFeeDto | null>(null);
    const [feeInput, setFeeInput] = useState<string>('');
    const [submittingFee, setSubmittingFee] = useState<boolean>(false);

    const formatCurrency = (val: number) => {
        return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
    };

    const formatDate = (val: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
            }).format(new Date(val));
        } catch {
            return val;
        }
    };

    const fetchRevenueReport = async () => {
        setRevLoading(true);
        try {
            const res = await billingApi.admin.getRevenueReport(fromDate || undefined, toDate || undefined);
            if (res.success && res.data) {
                setRevenueReport(res.data);
            }
        } catch (err: any) {
            console.error('Lỗi khi tải báo cáo doanh thu:', err);
            showAlert(err?.message || 'Không thể tải báo cáo doanh thu.', 'Lỗi', 'error');
        } finally {
            setRevLoading(false);
        }
    };

    const fetchSpecialtyFees = async () => {
        setFeesLoading(true);
        try {
            const res = await billingApi.admin.getSpecialtyFees();
            if (res.success && res.data) {
                setSpecialtyFees(res.data);
            }
        } catch (err: any) {
            console.error('Lỗi khi tải biểu phí chuyên khoa:', err);
            showAlert(err?.message || 'Không thể tải danh sách biểu phí chuyên khoa.', 'Lỗi', 'error');
        } finally {
            setFeesLoading(false);
        }
    };

    useEffect(() => {
        if (activeTab === 'revenue') {
            fetchRevenueReport();
        } else {
            fetchSpecialtyFees();
        }
    }, [activeTab]);

    const handleFilterRevenue = (e: React.FormEvent) => {
        e.preventDefault();
        fetchRevenueReport();
    };

    const handleOpenEditFee = (spec: SpecialtyFeeDto) => {
        setSelectedSpecialty(spec);
        setFeeInput(spec.consultationFee.toString());
        setEditModalOpen(true);
    };

    const handleSaveFee = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!selectedSpecialty) return;

        const newFee = parseFloat(feeInput);
        if (isNaN(newFee) || newFee < 0) {
            showAlert('Mức phí khám phải là số lớn hơn hoặc bằng 0.', 'Dữ liệu không hợp lệ', 'warning');
            return;
        }

        setSubmittingFee(true);
        try {
            const res = await billingApi.admin.updateSpecialtyFee(selectedSpecialty.id, {
                consultationFee: newFee,
            });

            if (res.success && res.data) {
                showAlert(`Cập nhật phí khám chuyên khoa "${selectedSpecialty.name}" thành ${formatCurrency(newFee)} thành công!`, 'Thành công', 'success');
                setEditModalOpen(false);
                fetchSpecialtyFees();
            }
        } catch (err: any) {
            showAlert(err?.message || 'Không thể cập nhật biểu phí.', 'Lỗi cập nhật', 'error');
        } finally {
            setSubmittingFee(false);
        }
    };

    return (
        <div className={styles.container}>
            {/* Header */}
            <div className={styles.headerRow}>
                <div className={styles.titleArea}>
                    <TrendingUp size={28} color="var(--c-primary)" />
                    <div>
                        <h2>Doanh thu & Biểu phí phòng khám</h2>
                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                            Theo dõi thực thu tài chính và cấu hình mức phí khám theo chuyên khoa (Dữ liệu demo)
                        </div>
                    </div>
                </div>

                <div style={{ display: 'flex', gap: '8px' }}>
                    <button
                        type="button"
                        className={activeTab === 'revenue' ? 'btn-primary' : 'btn-secondary'}
                        onClick={() => setActiveTab('revenue')}
                    >
                        <TrendingUp size={15} /> Báo cáo doanh thu
                    </button>
                    <button
                        type="button"
                        className={activeTab === 'fees' ? 'btn-primary' : 'btn-secondary'}
                        onClick={() => setActiveTab('fees')}
                    >
                        <Stethoscope size={15} /> Quản lý biểu phí
                    </button>
                </div>
            </div>

            {/* TAB 1: REVENUE REPORT */}
            {activeTab === 'revenue' && (
                <div>
                    {/* Date filter */}
                    <div className={styles.filterCard}>
                        <form onSubmit={handleFilterRevenue} className={styles.filterForm}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <Calendar size={18} color="var(--c-muted)" />
                                <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>Khoảng thời gian:</span>
                            </div>

                            <input
                                type="date"
                                className="form-input"
                                style={{ width: '160px' }}
                                value={fromDate}
                                onChange={(e) => setFromDate(e.target.value)}
                                title="Từ ngày"
                            />
                            <span>-</span>
                            <input
                                type="date"
                                className="form-input"
                                style={{ width: '160px' }}
                                value={toDate}
                                onChange={(e) => setToDate(e.target.value)}
                                title="Đến ngày"
                            />

                            <button type="submit" className="btn-secondary">
                                Xem báo cáo
                            </button>
                            <button
                                type="button"
                                className="btn-secondary"
                                onClick={() => {
                                    setFromDate(thirtyDaysAgoStr);
                                    setToDate(todayStr);
                                    fetchRevenueReport();
                                }}
                            >
                                <RefreshCw size={14} /> 30 ngày gần nhất
                            </button>
                        </form>
                    </div>

                    {revLoading ? (
                        <div style={{ padding: '60px', textAlign: 'center', color: 'var(--c-muted)' }}>
                            Đang tải báo cáo doanh thu...
                        </div>
                    ) : revenueReport ? (
                        <>
                            {/* Summary KPI Cards */}
                            <div className={styles.kpiGrid}>
                                <div className={styles.kpiCard}>
                                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-success-bg)', color: 'var(--c-success)' }}>
                                        <DollarSign size={24} />
                                    </div>
                                    <div className={styles.kpiContent}>
                                        <span className={styles.kpiLabel}>Tổng thực thu</span>
                                        <span className={styles.kpiValue} style={{ color: 'var(--c-success)' }}>
                                            {formatCurrency(revenueReport.totalRevenue)}
                                        </span>
                                    </div>
                                </div>

                                <div className={styles.kpiCard}>
                                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-info-bg)', color: 'var(--c-info)' }}>
                                        <CheckCircle size={24} />
                                    </div>
                                    <div className={styles.kpiContent}>
                                        <span className={styles.kpiLabel}>Giao dịch thành công</span>
                                        <span className={styles.kpiValue}>
                                            {revenueReport.totalSucceededTransactions} lượt
                                        </span>
                                    </div>
                                </div>

                                <div className={styles.kpiCard}>
                                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-warning-bg)', color: 'var(--c-warning)' }}>
                                        <Clock size={24} />
                                    </div>
                                    <div className={styles.kpiContent}>
                                        <span className={styles.kpiLabel}>Chờ thanh toán</span>
                                        <span className={styles.kpiValue}>
                                            {revenueReport.statusBreakdown.unpaidCount} HĐ
                                        </span>
                                        <span style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                                            ({formatCurrency(revenueReport.statusBreakdown.unpaidAmount)})
                                        </span>
                                    </div>
                                </div>

                                <div className={styles.kpiCard}>
                                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-danger-bg)', color: 'var(--c-danger)' }}>
                                        <XCircle size={24} />
                                    </div>
                                    <div className={styles.kpiContent}>
                                        <span className={styles.kpiLabel}>Hóa đơn đã hủy</span>
                                        <span className={styles.kpiValue}>
                                            {revenueReport.statusBreakdown.cancelledCount} HĐ
                                        </span>
                                        <span style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                                            ({formatCurrency(revenueReport.statusBreakdown.cancelledAmount)})
                                        </span>
                                    </div>
                                </div>
                            </div>

                            {/* Daily Breakdown Table */}
                            <div className="card" style={{ padding: 0 }}>
                                <div style={{ padding: '16px 20px', borderBottom: '1px solid var(--c-border)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <Layers size={18} color="var(--c-primary)" />
                                    <h3 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700 }}>
                                        Bảng chi tiết doanh thu theo từng ngày
                                    </h3>
                                </div>

                                <div className="table-responsive">
                                    <table className="table">
                                        <thead>
                                            <tr>
                                                <th>Ngày giao dịch</th>
                                                <th style={{ textAlign: 'center' }}>Số GD thành công</th>
                                                <th style={{ textAlign: 'center' }}>Số HĐ đã thu</th>
                                                <th style={{ textAlign: 'right' }}>Doanh thu trong ngày</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {revenueReport.dailyBreakdown.length === 0 ? (
                                                <tr>
                                                    <td colSpan={4} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                                        Không có giao dịch nào phát sinh trong khoảng thời gian đã chọn.
                                                    </td>
                                                </tr>
                                            ) : (
                                                revenueReport.dailyBreakdown.map((row, idx) => (
                                                    <tr key={idx}>
                                                        <td style={{ fontWeight: 600 }}>{formatDate(row.date)}</td>
                                                        <td style={{ textAlign: 'center' }}>{row.succeededPaymentsCount}</td>
                                                        <td style={{ textAlign: 'center' }}>{row.paidInvoicesCount}</td>
                                                        <td style={{ textAlign: 'right', fontWeight: 700, color: 'var(--c-success)' }}>
                                                            {formatCurrency(row.revenue)}
                                                        </td>
                                                    </tr>
                                                ))
                                            )}
                                        </tbody>
                                    </table>
                                </div>
                            </div>
                        </>
                    ) : (
                        <div className="empty-state">
                            <h3>Không có dữ liệu báo cáo</h3>
                        </div>
                    )}
                </div>
            )}

            {/* TAB 2: SPECIALTY CONSULTATION FEES */}
            {activeTab === 'fees' && (
                <div className="card" style={{ padding: 0 }}>
                    <div style={{ padding: '16px 20px', borderBottom: '1px solid var(--c-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div>
                            <h3 style={{ margin: 0, fontSize: '1.05rem', fontWeight: 700 }}>
                                Danh mục biểu phí khám chuyên khoa
                            </h3>
                            <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                                Mức phí này sẽ được tự động snapshot vào hóa đơn khi lượt khám hoàn thành.
                            </div>
                        </div>

                        <button type="button" className="btn-secondary" onClick={fetchSpecialtyFees}>
                            <RefreshCw size={14} /> Làm mới
                        </button>
                    </div>

                    {feesLoading ? (
                        <div style={{ padding: '48px', textAlign: 'center', color: 'var(--c-muted)' }}>
                            Đang tải biểu phí chuyên khoa...
                        </div>
                    ) : (
                        <div className="table-responsive">
                            <table className="table">
                                <thead>
                                    <tr>
                                        <th>Mã chuyên khoa</th>
                                        <th>Tên chuyên khoa</th>
                                        <th style={{ textAlign: 'right' }}>Phí khám hiện tại</th>
                                        <th style={{ textAlign: 'center' }}>Trạng thái</th>
                                        <th style={{ textAlign: 'right' }}>Cập nhật</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {specialtyFees.map((spec) => (
                                        <tr key={spec.id}>
                                            <td style={{ fontWeight: 700, color: 'var(--c-navy-dark)' }}>
                                                {spec.specialtyCode}
                                            </td>
                                            <td style={{ fontWeight: 600 }}>{spec.name}</td>
                                            <td style={{ textAlign: 'right', fontWeight: 700, color: 'var(--c-primary)', fontSize: '1rem' }}>
                                                {formatCurrency(spec.consultationFee)}
                                            </td>
                                            <td style={{ textAlign: 'center' }}>
                                                {spec.isActive ? (
                                                    <span className="badge badge-success">Đang hoạt động</span>
                                                ) : (
                                                    <span className="badge badge-muted">Tạm ngưng</span>
                                                )}
                                            </td>
                                            <td style={{ textAlign: 'right' }}>
                                                <button
                                                    type="button"
                                                    className="btn-secondary"
                                                    style={{ padding: '4px 10px', fontSize: '0.8rem' }}
                                                    onClick={() => handleOpenEditFee(spec)}
                                                >
                                                    <Edit size={13} /> Sửa mức phí
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </div>
            )}

            {/* Edit Specialty Fee Modal */}
            {editModalOpen && selectedSpecialty && (
                <div className={styles.modalOverlay} onClick={() => !submittingFee && setEditModalOpen(false)}>
                    <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
                        <div className={styles.modalHeader}>
                            <h3>Cập nhật phí khám: {selectedSpecialty.name}</h3>
                            <button
                                type="button"
                                className="btn-secondary"
                                style={{ padding: '4px 8px' }}
                                disabled={submittingFee}
                                onClick={() => setEditModalOpen(false)}
                            >
                                <X size={16} />
                            </button>
                        </div>
                        <form onSubmit={handleSaveFee}>
                            <div className={styles.modalBody}>
                                <div style={{ marginBottom: '14px', fontSize: '0.875rem' }}>
                                    <div><strong>Mã chuyên khoa:</strong> {selectedSpecialty.specialtyCode}</div>
                                    <div style={{ marginTop: '4px' }}>
                                        <strong>Mức phí hiện tại:</strong>{' '}
                                        <span style={{ color: 'var(--c-primary)', fontWeight: 700 }}>
                                            {formatCurrency(selectedSpecialty.consultationFee)}
                                        </span>
                                    </div>
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Mức phí khám mới (VNĐ) *</label>
                                    <input
                                        type="number"
                                        className="form-input"
                                        value={feeInput}
                                        onChange={(e) => setFeeInput(e.target.value)}
                                        required
                                        min="0"
                                        step="1000"
                                    />
                                    <span style={{ fontSize: '0.75rem', color: 'var(--c-muted)', display: 'block', marginTop: '4px' }}>
                                        Mức phí áp dụng cho tất cả các ca khám mới hoàn thành thuộc chuyên khoa này.
                                    </span>
                                </div>
                            </div>
                            <div className={styles.modalFooter}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    disabled={submittingFee}
                                    onClick={() => setEditModalOpen(false)}
                                >
                                    Hủy bỏ
                                </button>
                                <button
                                    type="submit"
                                    className="btn-primary"
                                    disabled={submittingFee}
                                >
                                    {submittingFee ? 'Đang lưu...' : 'Lưu mức phí'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
