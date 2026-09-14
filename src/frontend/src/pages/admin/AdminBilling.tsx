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
import { diagnosticApi } from '../../api/diagnosticApi';
import type {
    RevenueReportDto,
    SpecialtyFeeDto,
    DiagnosticServiceDto,
} from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import styles from './AdminBilling.module.css';

export const AdminBilling: React.FC = () => {
    const { showAlert } = useDialog();

    // Active View Tab: 'revenue', 'fees', or 'diagnostics'
    const [activeTab, setActiveTab] = useState<'revenue' | 'fees' | 'diagnostics'>('revenue');

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

    // Diagnostic services state
    const [diagnosticServices, setDiagnosticServices] = useState<DiagnosticServiceDto[]>([]);
    const [diagLoading, setDiagLoading] = useState<boolean>(true);
    const [editDiagModalOpen, setEditDiagModalOpen] = useState<boolean>(false);
    const [selectedDiag, setSelectedDiag] = useState<DiagnosticServiceDto | null>(null);
    const [diagPriceInput, setDiagPriceInput] = useState<string>('');
    const [submittingDiagPrice, setSubmittingDiagPrice] = useState<boolean>(false);

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

    const fetchDiagnosticServices = async () => {
        setDiagLoading(true);
        try {
            const res = await diagnosticApi.getDiagnosticPricing();
            if (res.success && res.data) {
                setDiagnosticServices(res.data);
            }
        } catch (err: any) {
            console.error('Lỗi khi tải biểu phí cận lâm sàng:', err);
            showAlert(err?.message || 'Không thể tải bảng giá cận lâm sàng.', 'Lỗi', 'error');
        } finally {
            setDiagLoading(false);
        }
    };

    useEffect(() => {
        if (activeTab === 'revenue') {
            fetchRevenueReport();
        } else if (activeTab === 'fees') {
            fetchSpecialtyFees();
        } else if (activeTab === 'diagnostics') {
            fetchDiagnosticServices();
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

    const handleOpenEditDiag = (svc: DiagnosticServiceDto) => {
        setSelectedDiag(svc);
        setDiagPriceInput((svc.price ?? 0).toString());
        setEditDiagModalOpen(true);
    };

    const handleSaveDiagPrice = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!selectedDiag) return;

        const newPrice = parseFloat(diagPriceInput);
        if (isNaN(newPrice) || newPrice < 0) {
            showAlert('Đơn giá dịch vụ phải là số lớn hơn hoặc bằng 0.', 'Dữ liệu không hợp lệ', 'warning');
            return;
        }

        setSubmittingDiagPrice(true);
        try {
            const res = await diagnosticApi.updateDiagnosticPrice(selectedDiag.id, newPrice);
            if (res.success) {
                showAlert(`Cập nhật giá dịch vụ "${selectedDiag.name}" thành ${formatCurrency(newPrice)} thành công!`, 'Thành công', 'success');
                setEditDiagModalOpen(false);
                fetchDiagnosticServices();
            }
        } catch (err: any) {
            showAlert(err?.message || 'Không thể cập nhật đơn giá.', 'Lỗi cập nhật', 'error');
        } finally {
            setSubmittingDiagPrice(false);
        }
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
                            Theo dõi thực thu tài chính, cấu hình phí khám chuyên khoa và bảng giá cận lâm sàng
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
                        <Stethoscope size={15} /> Phí khám chuyên khoa
                    </button>
                    <button
                        type="button"
                        className={activeTab === 'diagnostics' ? 'btn-primary' : 'btn-secondary'}
                        onClick={() => setActiveTab('diagnostics')}
                    >
                        <Layers size={15} /> Bảng giá Cận lâm sàng
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

            {/* TAB 3: DIAGNOSTIC PRICING */}
            {activeTab === 'diagnostics' && (
                <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                        <div>
                            <h3 style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--c-text)' }}>
                                Bảng giá Dịch vụ Cận lâm sàng ({diagnosticServices.length} dịch vụ)
                            </h3>
                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                                Bảng giá áp dụng khi bác sĩ chỉ định và tính hóa đơn viện phí thực tế
                            </div>
                        </div>
                        <button
                            type="button"
                            className="btn-secondary"
                            onClick={fetchDiagnosticServices}
                            disabled={diagLoading}
                            style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                        >
                            <RefreshCw size={14} className={diagLoading ? 'spin' : ''} /> Làm mới
                        </button>
                    </div>

                    {diagLoading ? (
                        <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                            Đang tải bảng giá cận lâm sàng...
                        </div>
                    ) : (
                        <div className={styles.tableCard}>
                            <table className={styles.table}>
                                <thead>
                                    <tr>
                                        <th>Mã dịch vụ</th>
                                        <th>Tên kỹ thuật / dịch vụ</th>
                                        <th>Loại dịch vụ</th>
                                        <th>Mô tả quy trình</th>
                                        <th style={{ textAlign: 'right' }}>Đơn giá (VNĐ)</th>
                                        <th style={{ textAlign: 'right' }}>Thao tác</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {diagnosticServices.map((svc) => (
                                        <tr key={svc.id}>
                                            <td>
                                                <span className={styles.codeBadge}>{svc.code}</span>
                                            </td>
                                            <td style={{ fontWeight: 600, color: 'var(--c-text)' }}>
                                                {svc.name}
                                            </td>
                                            <td>
                                                <span style={{ fontSize: '0.8rem', background: '#e0f2fe', color: '#0369a1', padding: '2px 8px', borderRadius: '4px' }}>
                                                    {svc.category === 'Laboratory' ? 'Xét nghiệm' : svc.category === 'Ultrasound' ? 'Siêu âm' : svc.category === 'Imaging' ? 'Chẩn đoán hình ảnh' : 'Khác'}
                                                </span>
                                            </td>
                                            <td style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>
                                                {svc.preparationInstructions || '-'}
                                            </td>
                                            <td style={{ textAlign: 'right', fontWeight: 700, color: 'var(--c-primary)' }}>
                                                {formatCurrency(svc.price ?? 0)}
                                            </td>
                                            <td style={{ textAlign: 'right' }}>
                                                <button
                                                    type="button"
                                                    className="btn-secondary"
                                                    style={{ padding: '4px 10px', fontSize: '0.8rem' }}
                                                    onClick={() => handleOpenEditDiag(svc)}
                                                >
                                                    <Edit size={13} /> Sửa giá
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                    {diagnosticServices.length === 0 && (
                                        <tr>
                                            <td colSpan={6} style={{ textAlign: 'center', padding: '32px', color: 'var(--c-muted)' }}>
                                                Chưa có dịch vụ cận lâm sàng nào trong hệ thống.
                                            </td>
                                        </tr>
                                    )}
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

            {/* Edit Diagnostic Price Modal */}
            {editDiagModalOpen && selectedDiag && (
                <div className={styles.modalOverlay} onClick={() => !submittingDiagPrice && setEditDiagModalOpen(false)}>
                    <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
                        <div className={styles.modalHeader}>
                            <h3>Cập nhật giá dịch vụ: {selectedDiag.name}</h3>
                            <button
                                type="button"
                                className="btn-secondary"
                                style={{ padding: '4px 8px' }}
                                disabled={submittingDiagPrice}
                                onClick={() => setEditDiagModalOpen(false)}
                            >
                                <X size={16} />
                            </button>
                        </div>
                        <form onSubmit={handleSaveDiagPrice}>
                            <div className={styles.modalBody}>
                                <div style={{ marginBottom: '14px', fontSize: '0.875rem' }}>
                                    <div><strong>Mã dịch vụ:</strong> {selectedDiag.code}</div>
                                    <div style={{ marginTop: '4px' }}>
                                        <strong>Đơn giá hiện tại:</strong>{' '}
                                        <span style={{ color: 'var(--c-primary)', fontWeight: 700 }}>
                                            {formatCurrency(selectedDiag.price ?? 0)}
                                        </span>
                                    </div>
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Đơn giá mới (VNĐ) *</label>
                                    <input
                                        type="number"
                                        className="form-input"
                                        value={diagPriceInput}
                                        onChange={(e) => setDiagPriceInput(e.target.value)}
                                        required
                                        min="0"
                                        step="1000"
                                    />
                                    <span style={{ fontSize: '0.75rem', color: 'var(--c-muted)', display: 'block', marginTop: '4px' }}>
                                        Mức giá áp dụng cho tất cả chỉ định cận lâm sàng mới và tính hóa đơn viện phí.
                                    </span>
                                </div>
                            </div>
                            <div className={styles.modalFooter}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    disabled={submittingDiagPrice}
                                    onClick={() => setEditDiagModalOpen(false)}
                                >
                                    Hủy bỏ
                                </button>
                                <button
                                    type="submit"
                                    className="btn-primary"
                                    disabled={submittingDiagPrice}
                                >
                                    {submittingDiagPrice ? 'Đang lưu...' : 'Lưu đơn giá'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
