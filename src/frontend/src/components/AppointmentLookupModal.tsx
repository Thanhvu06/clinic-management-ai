import React, { useState } from 'react';
import { Search, Calendar, Clock, User, Stethoscope, AlertCircle, X, CheckCircle, Clock4, XCircle } from 'lucide-react';
import axiosClient from '../api/axiosClient';
import type { ApiResponse } from '../types';

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
    const [results, setResults] = useState<AppointmentLookupDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [searched, setSearched] = useState(false);
    const [errorMsg, setErrorMsg] = useState('');

    if (!isOpen) return null;

    const handleSearch = async (e: React.FormEvent) => {
        e.preventDefault();
        const trimmed = query.trim();
        if (!trimmed) {
            setErrorMsg('Vui lòng nhập mã lịch hẹn (ví dụ: APT-...) hoặc số điện thoại.');
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
            const res = await axiosClient.get<any, ApiResponse<AppointmentLookupDto[]>>(`/appointments/lookup?query=${encodeURIComponent(trimmed)}`);
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
                    <span style={{ backgroundColor: '#DEF7EC', color: '#03543F', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <CheckCircle size={14} /> Đã xác nhận
                    </span>
                );
            case 'Pending':
                return (
                    <span style={{ backgroundColor: '#FEF08A', color: '#854D0E', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <Clock4 size={14} /> Đang chờ duyệt
                    </span>
                );
            case 'Completed':
                return (
                    <span style={{ backgroundColor: '#E0E7FF', color: '#3730A3', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <CheckCircle size={14} /> Đã khám xong
                    </span>
                );
            case 'Cancelled':
                return (
                    <span style={{ backgroundColor: '#FEE2E2', color: '#991B1B', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <XCircle size={14} /> Đã hủy
                    </span>
                );
            default:
                return (
                    <span style={{ backgroundColor: '#F3F4F6', color: '#374151', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600 }}>
                        {status}
                    </span>
                );
        }
    };

    return (
        <div style={{
            position: 'fixed',
            inset: 0,
            backgroundColor: 'rgba(15, 23, 42, 0.65)',
            backdropFilter: 'blur(4px)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 9999,
            padding: '16px'
        }}>
            <div style={{
                backgroundColor: 'white',
                borderRadius: '16px',
                width: '100%',
                maxWidth: '620px',
                maxHeight: '90vh',
                overflowY: 'auto',
                boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                display: 'flex',
                flexDirection: 'column'
            }}>
                {/* Header */}
                <div style={{
                    padding: '20px 24px',
                    borderBottom: '1px solid #e2e8f0',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    backgroundColor: '#f8fafc',
                    borderTopLeftRadius: '16px',
                    borderTopRightRadius: '16px'
                }}>
                    <div>
                        <h3 style={{ margin: 0, color: '#0f172a', fontSize: '1.25rem', fontWeight: 700 }}>
                            Tra cứu lịch hẹn khám bệnh
                        </h3>
                        <p style={{ margin: '4px 0 0', color: '#64748b', fontSize: '0.875rem' }}>
                            Nhập mã lịch hẹn (APT-...) hoặc số điện thoại đã đăng ký
                        </p>
                    </div>
                    <button
                        onClick={onClose}
                        style={{
                            background: 'none',
                            border: 'none',
                            color: '#64748b',
                            cursor: 'pointer',
                            padding: '6px',
                            borderRadius: '8px',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center'
                        }}
                    >
                        <X size={20} />
                    </button>
                </div>

                {/* Body */}
                <div style={{ padding: '24px' }}>
                    <form onSubmit={handleSearch} style={{ display: 'flex', gap: '10px', marginBottom: '20px' }}>
                        <div style={{ position: 'relative', flex: 1 }}>
                            <Search size={18} style={{ position: 'absolute', left: '14px', top: '13px', color: '#94a3b8' }} />
                            <input
                                type="text"
                                placeholder="Ví dụ: APT-260829-0001 hoặc 0900000004"
                                value={query}
                                onChange={(e) => setQuery(e.target.value)}
                                style={{
                                    width: '100%',
                                    padding: '10px 14px 10px 42px',
                                    borderRadius: '10px',
                                    border: '1.5px solid #cbd5e1',
                                    fontSize: '0.95rem',
                                    outline: 'none',
                                    boxSizing: 'border-box'
                                }}
                            />
                        </div>
                        <button
                            type="submit"
                            disabled={loading}
                            style={{
                                padding: '10px 20px',
                                backgroundColor: '#0284c7',
                                color: 'white',
                                border: 'none',
                                borderRadius: '10px',
                                fontWeight: 600,
                                cursor: loading ? 'not-allowed' : 'pointer',
                                transition: 'background-color 0.2s',
                                whiteSpace: 'nowrap'
                            }}
                        >
                            {loading ? 'Đang tìm...' : 'Tra cứu'}
                        </button>
                    </form>

                    {errorMsg && (
                        <div style={{
                            padding: '12px 16px',
                            backgroundColor: '#fef2f2',
                            border: '1px solid #fecaca',
                            borderRadius: '10px',
                            color: '#991b1b',
                            fontSize: '0.875rem',
                            marginBottom: '16px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px'
                        }}>
                            <AlertCircle size={18} />
                            <span>{errorMsg}</span>
                        </div>
                    )}

                    {/* Results */}
                    {searched && !loading && (
                        <div>
                            {results.length === 0 ? (
                                <div style={{
                                    textAlign: 'center',
                                    padding: '36px 16px',
                                    color: '#64748b'
                                }}>
                                    <AlertCircle size={40} style={{ margin: '0 auto 12px', color: '#94a3b8' }} />
                                    <h4 style={{ margin: '0 0 6px', color: '#334155' }}>Không tìm thấy lịch hẹn</h4>
                                    <p style={{ margin: 0, fontSize: '0.875rem' }}>
                                        Vui lòng kiểm tra lại mã lịch hẹn hoặc số điện thoại chính xác.
                                    </p>
                                </div>
                            ) : (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                                    <div style={{ fontSize: '0.875rem', color: '#64748b', fontWeight: 600 }}>
                                        Tìm thấy {results.length} lịch hẹn:
                                    </div>
                                    {results.map((item, idx) => (
                                        <div
                                            key={idx}
                                            style={{
                                                border: '1px solid #e2e8f0',
                                                borderRadius: '12px',
                                                padding: '16px',
                                                backgroundColor: '#ffffff',
                                                boxShadow: '0 2px 4px rgba(0,0,0,0.02)'
                                            }}
                                        >
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px', borderBottom: '1px solid #f1f5f9', paddingBottom: '10px' }}>
                                                <div>
                                                    <span style={{ fontSize: '0.8rem', color: '#64748b', textTransform: 'uppercase', fontWeight: 600 }}>Mã lịch hẹn</span>
                                                    <div style={{ fontSize: '1rem', fontWeight: 700, color: '#0284c7' }}>{item.appointmentCode}</div>
                                                </div>
                                                <div>{getStatusBadge(item.status)}</div>
                                            </div>

                                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '10px', fontSize: '0.875rem' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#334155' }}>
                                                    <Calendar size={16} color="#0284c7" />
                                                    <span>Ngày khám: <strong>{item.appointmentDate}</strong></span>
                                                </div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#334155' }}>
                                                    <Clock size={16} color="#0284c7" />
                                                    <span>Giờ khám: <strong>{item.startTime} - {item.endTime}</strong></span>
                                                </div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#334155' }}>
                                                    <Stethoscope size={16} color="#0284c7" />
                                                    <span>Chuyên khoa: <strong>{item.specialtyName}</strong></span>
                                                </div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#334155' }}>
                                                    <User size={16} color="#0284c7" />
                                                    <span>Bác sĩ: <strong>{item.doctorName}</strong></span>
                                                </div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#64748b', gridColumn: '1 / -1', borderTop: '1px dashed #f1f5f9', paddingTop: '8px', marginTop: '4px' }}>
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
                <div style={{
                    padding: '14px 24px',
                    borderTop: '1px solid #e2e8f0',
                    display: 'flex',
                    justifyContent: 'flex-end',
                    backgroundColor: '#f8fafc',
                    borderBottomLeftRadius: '16px',
                    borderBottomRightRadius: '16px'
                }}>
                    <button
                        onClick={onClose}
                        style={{
                            padding: '8px 18px',
                            backgroundColor: '#e2e8f0',
                            color: '#334155',
                            border: 'none',
                            borderRadius: '8px',
                            fontWeight: 600,
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
