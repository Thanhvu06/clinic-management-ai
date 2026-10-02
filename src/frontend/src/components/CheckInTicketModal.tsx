import React from 'react';
import { Printer, X, CheckCircle, Clock, MapPin, User, Stethoscope } from 'lucide-react';
import type { CheckInTicketDto } from '../types';

interface CheckInTicketModalProps {
    isOpen: boolean;
    ticket: CheckInTicketDto | null;
    onClose: () => void;
}

export const CheckInTicketModal: React.FC<CheckInTicketModalProps> = ({
    isOpen,
    ticket,
    onClose
}) => {
    if (!isOpen || !ticket) return null;

    const handlePrint = () => {
        window.print();
    };

    const formatDateTime = (isoString?: string) => {
        if (!isoString) return '';
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit'
            }).format(new Date(isoString));
        } catch {
            return isoString;
        }
    };

    return (
        <div style={{
            position: 'fixed',
            inset: 0,
            backgroundColor: 'rgba(0, 0, 0, 0.6)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1000,
            padding: '20px'
        }}>
            <div style={{
                background: '#ffffff',
                borderRadius: '16px',
                width: '100%',
                maxWidth: '480px',
                boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                overflow: 'hidden',
                display: 'flex',
                flexDirection: 'column'
            }}>
                {/* Header (Screen only) */}
                <div style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    padding: '16px 20px',
                    borderBottom: '1px solid #e2e8f0',
                    background: '#f8fafc'
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#0f766e', fontWeight: 600 }}>
                        <CheckCircle size={20} />
                        <span>Phiếu tiếp nhận khám</span>
                    </div>
                    <button
                        onClick={onClose}
                        style={{
                            background: 'none',
                            border: 'none',
                            cursor: 'pointer',
                            color: '#64748b',
                            padding: '4px',
                            display: 'flex',
                            alignItems: 'center'
                        }}
                        aria-label="Đóng"
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Printable Ticket Area */}
                <div id="printable-ticket" style={{
                    padding: '24px',
                    color: '#1e293b',
                    fontFamily: 'Inter, system-ui, sans-serif'
                }}>
                    {/* Facility Brand */}
                    <div style={{ textAlign: 'center', borderBottom: '2px dashed #cbd5e1', paddingBottom: '16px', marginBottom: '16px' }}>
                        <div style={{ fontSize: '1.1rem', fontWeight: 700, textTransform: 'uppercase', color: '#0f766e' }}>
                            {ticket.facilityName || 'HỆ THỐNG PHÒNG KHÁM CLINICCARE'}
                        </div>
                        <div style={{ fontSize: '0.85rem', color: '#64748b', marginTop: '4px' }}>
                            PHIẾU KHÁM BỆNH & SỐ THỨ TỰ
                        </div>
                    </div>

                    {/* Prominent Queue Display */}
                    <div style={{
                        textAlign: 'center',
                        background: '#f0fdfa',
                        border: '2px solid #0d9488',
                        borderRadius: '12px',
                        padding: '16px',
                        marginBottom: '20px'
                    }}>
                        <div style={{ fontSize: '0.85rem', fontWeight: 600, color: '#0f766e', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                            Số thứ tự khám
                        </div>
                        <div style={{ fontSize: '3rem', fontWeight: 800, color: '#0f766e', lineHeight: 1.1, margin: '8px 0' }}>
                            {ticket.queueDisplay || ticket.queueNumber}
                        </div>
                        <div style={{ fontSize: '0.85rem', color: '#64748b' }}>
                            Mã lượt khám: <strong>{ticket.visitCode}</strong>
                        </div>
                    </div>

                    {/* Patient and Routing Details */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', fontSize: '0.92rem' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                            <span style={{ color: '#64748b', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <User size={15} /> Bệnh nhân:
                            </span>
                            <span style={{ fontWeight: 700 }}>{ticket.patientName}</span>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                            <span style={{ color: '#64748b' }}>Mã bệnh án (MRN):</span>
                            <span style={{ fontWeight: 600, color: '#0f766e' }}>{ticket.medicalRecordNumber || '---'}</span>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                            <span style={{ color: '#64748b' }}>Số điện thoại:</span>
                            <span>{ticket.phoneNumber || '---'}</span>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                            <span style={{ color: '#64748b', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <MapPin size={15} /> Khoa tiếp nhận:
                            </span>
                            <span style={{ fontWeight: 600 }}>{ticket.departmentName}</span>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                            <span style={{ color: '#64748b' }}>Phòng khám:</span>
                            <span style={{ fontWeight: 600, color: '#b45309' }}>
                                {ticket.roomNumber || 'Theo hướng dẫn tại khoa'}
                            </span>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                            <span style={{ color: '#64748b', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Stethoscope size={15} /> Bác sĩ phụ trách:
                            </span>
                            <span style={{ fontWeight: 600 }}>{ticket.doctorName || 'Bác sĩ trực buồng khám'}</span>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0', borderBottom: '1px solid #f1f5f9' }}>
                            <span style={{ color: '#64748b', display: 'flex', alignItems: 'center', gap: '6px' }}>
                                <Clock size={15} /> Thời gian tiếp nhận:
                            </span>
                            <span>{formatDateTime(ticket.checkedInAtUtc)}</span>
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 0' }}>
                            <span style={{ color: '#64748b' }}>Nhân viên tiếp nhận:</span>
                            <span>{ticket.receptionistName || 'Lễ tân'}</span>
                        </div>
                    </div>

                    {/* Footer instructions */}
                    <div style={{
                        marginTop: '18px',
                        paddingTop: '12px',
                        borderTop: '2px dashed #cbd5e1',
                        textAlign: 'center',
                        fontSize: '0.8rem',
                        color: '#64748b'
                    }}>
                        * Quý khách vui lòng theo dõi màn hình gọi số tại sảnh chờ phòng khám.<br />
                        Chúc quý khách một ngày an lành và nhiều sức khỏe!
                    </div>
                </div>

                {/* Actions */}
                <div style={{
                    display: 'flex',
                    justifyContent: 'flex-end',
                    gap: '12px',
                    padding: '16px 20px',
                    borderTop: '1px solid #e2e8f0',
                    background: '#f8fafc'
                }}>
                    <button
                        type="button"
                        onClick={onClose}
                        style={{
                            padding: '8px 16px',
                            borderRadius: '8px',
                            border: '1px solid #cbd5e1',
                            background: '#ffffff',
                            color: '#334155',
                            fontWeight: 500,
                            cursor: 'pointer'
                        }}
                    >
                        Đóng
                    </button>
                    <button
                        type="button"
                        onClick={handlePrint}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            padding: '8px 18px',
                            borderRadius: '8px',
                            border: 'none',
                            background: '#0d9488',
                            color: '#ffffff',
                            fontWeight: 600,
                            cursor: 'pointer'
                        }}
                    >
                        <Printer size={16} />
                        In phiếu khám
                    </button>
                </div>
            </div>
        </div>
    );
};
