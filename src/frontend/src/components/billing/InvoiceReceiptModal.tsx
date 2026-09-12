import React from 'react';
import { X, Printer, CheckCircle, Clock, AlertCircle } from 'lucide-react';
import type { InvoiceDetailDto } from '../../types';
import { InvoiceStatus } from '../../types';
import styles from './InvoiceReceiptModal.module.css';

interface InvoiceReceiptModalProps {
    isOpen: boolean;
    onClose: () => void;
    invoice: InvoiceDetailDto | null;
    loading?: boolean;
}

export const InvoiceReceiptModal: React.FC<InvoiceReceiptModalProps> = ({
    isOpen,
    onClose,
    invoice,
    loading = false,
}) => {
    if (!isOpen) return null;

    const formatCurrency = (amount: number) => {
        return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
    };

    const formatDateTime = (dateStr?: string | null) => {
        if (!dateStr) return '---';
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit',
            }).format(new Date(dateStr));
        } catch {
            return dateStr;
        }
    };

    const handlePrint = () => {
        window.print();
    };

    const getStatusTag = (status: InvoiceStatus) => {
        switch (status) {
            case InvoiceStatus.Paid:
                return (
                    <span className="badge badge-success">
                        <CheckCircle size={13} /> Đã thanh toán
                    </span>
                );
            case InvoiceStatus.Unpaid:
                return (
                    <span className="badge badge-warning">
                        <Clock size={13} /> Chờ thanh toán
                    </span>
                );
            case InvoiceStatus.Cancelled:
                return (
                    <span className="badge badge-danger">
                        <AlertCircle size={13} /> Đã hủy
                    </span>
                );
            default:
                return null;
        }
    };

    const latestPayment = invoice?.payments && invoice.payments.length > 0
        ? invoice.payments[invoice.payments.length - 1]
        : null;

    return (
        <div className={styles.overlay} onClick={onClose}>
            <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
                <div className={styles.header}>
                    <h3 className={styles.headerTitle}>
                        Chi tiết hóa đơn & Phiếu thu
                    </h3>
                    <button type="button" className={styles.closeBtn} onClick={onClose} aria-label="Đóng">
                        <X size={20} />
                    </button>
                </div>

                <div className={styles.body}>
                    {loading || !invoice ? (
                        <div style={{ textAlign: 'center', padding: '40px', color: '#64748b' }}>
                            Đang tải chi tiết hóa đơn...
                        </div>
                    ) : (
                        <div className={styles.receiptContainer} id="printable-invoice-receipt">
                            {/* Clinic Header */}
                            <div className={styles.clinicHeader}>
                                <div className={styles.clinicName}>
                                    HỆ THỐNG PHÒNG KHÁM ĐA KHOA CLINICCARE AI
                                </div>
                                <div className={styles.clinicSub}>
                                    Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống.<br />
                                    <em>(Dữ liệu minh họa)</em>
                                </div>
                            </div>

                            {/* Receipt Title */}
                            <div className={styles.receiptTitle}>
                                <h3>HÓA ĐƠN THU TIỀN DỊCH VỤ</h3>
                                <div className={styles.meta}>
                                    Mã số: <strong>{invoice.invoiceCode}</strong> | Ngày lập: {formatDateTime(invoice.createdAtUtc)}
                                </div>
                            </div>

                            {/* Info Grid */}
                            <div className={styles.infoGrid}>
                                <div className={styles.infoRow}>
                                    <span className={styles.infoLabel}>Bệnh nhân:</span>
                                    <span className={styles.infoValue}>{invoice.patientName}</span>
                                </div>
                                <div className={styles.infoRow}>
                                    <span className={styles.infoLabel}>Số điện thoại:</span>
                                    <span className={styles.infoValue}>{invoice.patientPhone}</span>
                                </div>
                                <div className={styles.infoRow}>
                                    <span className={styles.infoLabel}>Nguồn phát sinh:</span>
                                    <span className={styles.infoValue}>
                                        {invoice.sourceTypeName} ({invoice.appointmentCode || invoice.registrationCode || 'N/A'})
                                    </span>
                                </div>
                                <div className={styles.infoRow}>
                                    <span className={styles.infoLabel}>Trạng thái:</span>
                                    <span className={styles.infoValue}>
                                        {getStatusTag(invoice.status)}
                                    </span>
                                </div>
                            </div>

                            {/* Items Table */}
                            <table className={styles.itemsTable}>
                                <thead>
                                    <tr>
                                        <th style={{ width: '40px' }}>STT</th>
                                        <th>Mô tả khoản mục</th>
                                        <th style={{ textAlign: 'center', width: '70px' }}>SL</th>
                                        <th style={{ textAlign: 'right', width: '130px' }}>Đơn giá</th>
                                        <th style={{ textAlign: 'right', width: '140px' }}>Thành tiền</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {invoice.items && invoice.items.length > 0 ? (
                                        invoice.items.map((item, idx) => (
                                            <tr key={item.id || idx}>
                                                <td>{idx + 1}</td>
                                                <td>
                                                    <div style={{ fontWeight: 600 }}>{item.description}</div>
                                                    <div style={{ fontSize: '0.75rem', color: '#64748b' }}>Mã: {item.itemCode}</div>
                                                </td>
                                                <td style={{ textAlign: 'center' }}>{item.quantity}</td>
                                                <td style={{ textAlign: 'right' }}>{formatCurrency(item.unitPrice)}</td>
                                                <td style={{ textAlign: 'right', fontWeight: 600 }}>{formatCurrency(item.lineTotal)}</td>
                                            </tr>
                                        ))
                                    ) : (
                                        <tr>
                                            <td colSpan={5} style={{ textAlign: 'center', color: '#94a3b8' }}>
                                                Không có chi tiết khoản mục.
                                            </td>
                                        </tr>
                                    )}
                                </tbody>
                            </table>

                            {/* Total Section */}
                            <div className={styles.totalSection}>
                                <div className={styles.totalRow}>
                                    <span style={{ color: '#64748b' }}>Tổng tiền dịch vụ:</span>
                                    <span>{formatCurrency(invoice.subtotal)}</span>
                                </div>
                                <div className={styles.totalRow}>
                                    <span style={{ fontWeight: 700, fontSize: '1rem', color: '#0f172a' }}>TỔNG CỘNG THANH TOÁN:</span>
                                    <span className={styles.grandTotal}>{formatCurrency(invoice.totalAmount)}</span>
                                </div>
                            </div>

                            {/* Payment details if Paid */}
                            {invoice.status === InvoiceStatus.Paid && latestPayment && (
                                <div className={styles.paymentBox}>
                                    <div className={styles.paymentTitle}>
                                        <CheckCircle size={16} /> Thông tin thanh toán xác nhận
                                    </div>
                                    <div className={styles.paymentGrid}>
                                        <div><strong>Mã giao dịch:</strong> {latestPayment.paymentCode}</div>
                                        <div><strong>Phương thức:</strong> {latestPayment.methodName}</div>
                                        <div><strong>Thời gian thu:</strong> {formatDateTime(latestPayment.receivedAtUtc)}</div>
                                        <div><strong>Thu ngân:</strong> {latestPayment.receivedByUserName || 'Lễ tân tiếp nhận'}</div>
                                        {latestPayment.referenceCode && (
                                            <div style={{ gridColumn: 'span 2' }}>
                                                <strong>Mã tham chiếu:</strong> {latestPayment.referenceCode}
                                            </div>
                                        )}
                                        {latestPayment.note && (
                                            <div style={{ gridColumn: 'span 2' }}>
                                                <strong>Ghi chú:</strong> {latestPayment.note}
                                            </div>
                                        )}
                                    </div>
                                </div>
                            )}

                            {/* Cancelled details if Cancelled */}
                            {invoice.status === InvoiceStatus.Cancelled && (
                                <div className={styles.cancelledBox}>
                                    <strong>Hóa đơn đã bị hủy:</strong> {invoice.cancellationReason || 'Không có lý do'}<br />
                                    <span style={{ fontSize: '0.8rem', color: '#64748b' }}>
                                        Thời điểm hủy: {formatDateTime(invoice.cancelledAtUtc)}
                                    </span>
                                </div>
                            )}

                            {/* Signatures for Print */}
                            <div className={styles.signatureSection}>
                                <div className={styles.signatureBox}>
                                    <div className={styles.signatureTitle}>Người nộp tiền</div>
                                    <div className={styles.signatureNote}>(Ký và ghi rõ họ tên)</div>
                                    <div className={styles.signatureName}>{invoice.patientName}</div>
                                </div>
                                <div className={styles.signatureBox}>
                                    <div className={styles.signatureTitle}>Thu ngân / Người lập phiếu</div>
                                    <div className={styles.signatureNote}>(Ký và ghi rõ họ tên)</div>
                                    <div className={styles.signatureName}>{invoice.paidByUserName || invoice.createdByUserName || 'Nhân viên lễ tân'}</div>
                                </div>
                            </div>
                        </div>
                    )}
                </div>

                <div className={styles.footer}>
                    {invoice && (
                        <button type="button" className="btn-secondary" onClick={handlePrint}>
                            <Printer size={16} /> In phiếu thu / hóa đơn
                        </button>
                    )}
                    <button type="button" className="btn-primary" onClick={onClose}>
                        Đóng
                    </button>
                </div>
            </div>
        </div>
    );
};
