import { getAppointmentStatusLabel } from '../utils/appointmentStatusLabels';
import { formatDateOnly } from '../utils/formatters';
import React, { useState } from 'react';
import { Search, Calendar, Clock, User, Stethoscope, AlertCircle, X, CheckCircle, Clock4, XCircle } from 'lucide-react';
import axiosClient from '../api/axiosClient';
import type { ApiResponse } from '../types';
import styles from './AppointmentLookupModal.module.css';

interface AppointmentLookupDto {
    appointmentCode: string;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    specialtyName: string;
    doctorName: string;
    status: string;
    maskedPatientName: string;
    maskedPhoneNumber: string;
}

interface AppointmentLookupModalProps {
    isOpen: boolean;
    onClose: () => void;
}

export const AppointmentLookupModal: React.FC<AppointmentLookupModalProps> = ({ isOpen, onClose }) => {
    const [query, setQuery] = useState('');
    const [phone, setPhone] = useState('');
    const [results, setResults] = useState<AppointmentLookupDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [searched, setSearched] = useState(false);
    const [errorMsg, setErrorMsg] = useState('');

    if (!isOpen) return null;

    const handleSearch = async (e: React.FormEvent) => {
        e.preventDefault();
        const trimmed = query.trim();
        if (!trimmed || !phone.trim()) {
            setErrorMsg('Vui lòng nhập mã lịch hẹn và số điện thoại chủ lịch hẹn.');
            return;
        }

        if (trimmed.length < 4) {
            setErrorMsg('Từ khóa tìm kiếm phải có ít nhất 4 ký tự.');
            return;
        }

        setErrorMsg('');
        setLoading(true);
        setSearched(true);

        try {
            const res = await axiosClient.get<any, ApiResponse<AppointmentLookupDto[]>>(`/appointments/lookup?query=${encodeURIComponent(trimmed)}&phone=${encodeURIComponent(phone.trim())}`);
            if (res.success && res.data) {
                setResults(res.data);
            } else {
                setResults([]);
            }
        } catch (err: any) {
            setErrorMsg(err?.message || 'Không thể tra cứu lịch hẹn vào lúc này. Vui lòng thử lại sau.');
            setResults([]);
        } finally {
            setLoading(false);
        }
    };

    const getStatusBadge = (status: string) => {
        switch (status) {
            case 'Confirmed':
                return (
                    <span className={styles.flexLayout1}>
                        <CheckCircle size={14} /> {getAppointmentStatusLabel(status, 'patient')}
                    </span>
                );
            case 'Pending':
                return (
                    <span className={styles.flexLayout2}>
                        <Clock4 size={14} /> {getAppointmentStatusLabel(status, 'patient')}
                    </span>
                );
            case 'Completed':
                return (
                    <span className={styles.flexLayout3}>
                        <CheckCircle size={14} /> {getAppointmentStatusLabel(status, 'patient')}
                    </span>
                );
            case 'Cancelled':
                return (
                    <span className={styles.flexLayout4}>
                        <XCircle size={14} /> {getAppointmentStatusLabel(status, 'patient')}
                    </span>
                );
            default:
                return (
                    <span className={styles.text1}>
                        {getAppointmentStatusLabel(status, 'patient')}
                    </span>
                );
        }
    };

    return (
        <div className={styles.overlay1}>
            <div className={styles.stackLayout1}>
                {/* Header */}
                <div className={styles.flexLayout5}>
                    <div>
                        <h3 className={styles.heading1}>
                            Tra cứu lịch hẹn khám bệnh
                        </h3>
                        <p className={styles.paragraph1}>
                            Nhập mã lịch hẹn (APT-...) và số điện thoại đã đăng ký
                        </p>
                    </div>
                    <button
                        onClick={onClose}
                        aria-label="Đóng cửa sổ tra cứu"
                        className={styles.flexLayout6}
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Body */}
                <div className={styles.spacing1}>
                    <form onSubmit={handleSearch} className={styles.flexLayout7}>
                        <div className={styles.spacing2}>
                            <Search size={18} className={styles.icon1} />
                            <input
                                type="text"
                                required
                                aria-label="Mã lịch hẹn"
                                placeholder="Ví dụ: APT-260829-0001"
                                value={query}
                                onChange={(e) => setQuery(e.target.value)}
                                className={styles.control1}
                            />
                        </div>
                        <input type="tel" aria-label="Số điện thoại chủ lịch hẹn" placeholder="Số điện thoại đã đăng ký" required
                            value={phone} onChange={event => setPhone(event.target.value)}
                            className={styles.control2} />
                        <button
                            type="submit"
                            disabled={loading}
                            className={[styles.button1, (loading ? styles.state1 : styles.state2)].join(' ')}
                        >
                            {loading ? 'Đang tìm...' : 'Tra cứu'}
                        </button>
                    </form>

                    {errorMsg && (
                        <div className={styles.flexLayout8}>
                            <AlertCircle size={18} />
                            <span>{errorMsg}</span>
                        </div>
                    )}

                    {/* Results */}
                    {searched && !loading && (
                        <div>
                            {results.length === 0 ? (
                                <div className={styles.tone1}>
                                    <AlertCircle size={40} className={styles.icon2} />
                                    <h4 className={styles.heading2}>Không tìm thấy lịch hẹn</h4>
                                    <p className={styles.paragraph2}>
                                        Vui lòng kiểm tra lại mã lịch hẹn hoặc số điện thoại chính xác.
                                    </p>
                                </div>
                            ) : (
                                <div className={styles.stackLayout2}>
                                    <div className={styles.text2}>
                                        Tìm thấy {results.length} lịch hẹn:
                                    </div>
                                    {results.map((item, idx) => (
                                        <div
                                            key={idx}
                                            className={styles.surface1}
                                        >
                                            <div className={styles.flexLayout9}>
                                                <div>
                                                    <span className={styles.text3}>Mã lịch hẹn</span>
                                                    <div className={styles.text4}>{item.appointmentCode}</div>
                                                </div>
                                                <div>{getStatusBadge(item.status)}</div>
                                            </div>

                                            <div className={styles.gridLayout1}>
                                                <div className={styles.flexLayout10}>
                                                    <Calendar size={16} color="var(--cc-color-info)" />
                                                    <span>Ngày khám: <strong>{formatDateOnly(item.appointmentDate)}</strong></span>
                                                </div>
                                                <div className={styles.flexLayout10}>
                                                    <Clock size={16} color="var(--cc-color-info)" />
                                                    <span>Giờ khám: <strong>{item.startTime} - {item.endTime}</strong></span>
                                                </div>
                                                <div className={styles.flexLayout10}>
                                                    <Stethoscope size={16} color="var(--cc-color-info)" />
                                                    <span>Chuyên khoa: <strong>{item.specialtyName}</strong></span>
                                                </div>
                                                <div className={styles.flexLayout10}>
                                                    <User size={16} color="var(--cc-color-info)" />
                                                    <span>Bác sĩ: <strong>{item.doctorName}</strong></span>
                                                </div>
                                                <div className={styles.flexLayout11}>
                                                    <span>Bệnh nhân: <strong>{item.maskedPatientName}</strong> ({item.maskedPhoneNumber})</span>
                                                </div>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    )}
                </div>

                {/* Footer */}
                <div className={styles.flexLayout12}>
                    <button
                        onClick={onClose}
                        className={styles.button2}
                    >
                        Đóng
                    </button>
                </div>
            </div>
        </div>
    );
};
