import React from 'react';
import { Printer, X, CheckCircle, Clock, MapPin, User, Stethoscope } from 'lucide-react';
import type { CheckInTicketDto } from '../types';
import styles from './CheckInTicketModal.module.css';
import { useScopedPrint } from '../hooks/useScopedPrint';

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
    const handlePrint = useScopedPrint('cc-ticket-printing', isOpen && !!ticket);
    if (!isOpen || !ticket) return null;

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
        <div className={styles.overlay1}>
            <div className={styles.stackLayout1}>
                {/* Header (Screen only) */}
                <div className={styles.flexLayout1}>
                    <div className={styles.flexLayout2}>
                        <CheckCircle size={20} />
                        <span>Phiếu tiếp nhận khám</span>
                    </div>
                    <button
                        onClick={onClose}
                        className={styles.flexLayout3}
                        aria-label="Đóng"
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Printable Ticket Area */}
                <div id="printable-ticket" className={styles.tone1}>
                    {/* Facility Brand */}
                    <div className={styles.spacing1}>
                        <div className={styles.text1}>
                            {ticket.facilityName || 'HỆ THỐNG PHÒNG KHÁM CLINICCARE'}
                        </div>
                        <div className={styles.text2}>
                            PHIẾU KHÁM BỆNH & SỐ THỨ TỰ
                        </div>
                    </div>

                    {/* Prominent Queue Display */}
                    <div className={styles.surface1}>
                        <div className={styles.text3}>
                            Số thứ tự khám
                        </div>
                        <div className={styles.text4}>
                            {ticket.queueDisplay || ticket.queueNumber}
                        </div>
                        <div className={styles.text5}>
                            Mã lượt khám: <strong>{ticket.visitCode}</strong>
                        </div>
                    </div>

                    {/* Patient and Routing Details */}
                    <div className={styles.stackLayout2}>
                        <div className={styles.flexLayout4}>
                            <span className={styles.flexLayout5}>
                                <User size={15} /> Bệnh nhân:
                            </span>
                            <span className={styles.text6}>{ticket.patientName}</span>
                        </div>

                        <div className={styles.flexLayout4}>
                            <span className={styles.text7}>Mã bệnh án (MRN):</span>
                            <span className={styles.text8}>{ticket.medicalRecordNumber || '---'}</span>
                        </div>

                        <div className={styles.flexLayout4}>
                            <span className={styles.text7}>Số điện thoại:</span>
                            <span>{ticket.phoneNumber || '---'}</span>
                        </div>

                        <div className={styles.flexLayout4}>
                            <span className={styles.flexLayout5}>
                                <MapPin size={15} /> Khoa tiếp nhận:
                            </span>
                            <span className={styles.text9}>{ticket.departmentName}</span>
                        </div>

                        <div className={styles.flexLayout4}>
                            <span className={styles.text7}>Phòng khám:</span>
                            <span className={styles.text10}>
                                {ticket.roomNumber || 'Theo hướng dẫn tại khoa'}
                            </span>
                        </div>

                        <div className={styles.flexLayout4}>
                            <span className={styles.flexLayout5}>
                                <Stethoscope size={15} /> Bác sĩ phụ trách:
                            </span>
                            <span className={styles.text9}>{ticket.doctorName || 'Bác sĩ trực buồng khám'}</span>
                        </div>

                        <div className={styles.flexLayout4}>
                            <span className={styles.flexLayout5}>
                                <Clock size={15} /> Thời gian tiếp nhận:
                            </span>
                            <span>{formatDateTime(ticket.checkedInAtUtc)}</span>
                        </div>

                        <div className={styles.flexLayout6}>
                            <span className={styles.text7}>Nhân viên tiếp nhận:</span>
                            <span>{ticket.receptionistName || 'Lễ tân'}</span>
                        </div>
                    </div>

                    {/* Footer instructions */}
                    <div className={styles.text11}>
                        * Quý khách vui lòng theo dõi màn hình gọi số tại sảnh chờ phòng khám.<br />
                        Chúc quý khách một ngày an lành và nhiều sức khỏe!
                    </div>
                </div>

                {/* Actions */}
                <div className={styles.flexLayout7}>
                    <button
                        type="button"
                        onClick={onClose}
                        className={styles.button1}
                    >
                        Đóng
                    </button>
                    <button
                        type="button"
                        onClick={handlePrint}
                        className={styles.flexLayout8}
                    >
                        <Printer size={16} />
                        In phiếu khám
                    </button>
                </div>
            </div>
        </div>
    );
};
