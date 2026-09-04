import React, { useState, useEffect } from 'react';
import { Breadcrumb } from '../../components/Breadcrumb';
import { FileText, CalendarDays, Pill, Printer, Search, CheckCircle, Clock4, AlertCircle } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';

interface PrescriptionItem {
    medicineId: number;
    name: string;
    unit: string;
    quantity: number;
    dosage: string;
    frequency: string;
    durationDays?: number;
    instructions?: string;
}

interface PatientPrescription {
    id: number;
    code: string;
    appointmentId: number;
    appointmentCode: string;
    appointmentDate: string;
    doctorName: string;
    specialtyName: string;
    diagnosis: string;
    status: string;
    notes?: string;
    createdAt: string;
    dispensedAt?: string;
    items: PrescriptionItem[];
}

export const PatientPrescriptions: React.FC = () => {
    const [prescriptions, setPrescriptions] = useState<PatientPrescription[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [errorMsg, setErrorMsg] = useState('');

    useEffect(() => {
        const fetchPrescriptions = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<PatientPrescription[]>>('/patients/me/prescriptions');
                if (res.success && res.data) {
                    setPrescriptions(res.data);
                } else {
                    setPrescriptions([]);
                }
            } catch (err: any) {
                console.error("Failed to fetch prescriptions", err);
                setErrorMsg(err?.message || "Không thể tải danh sách đơn thuốc.");
                setPrescriptions([]);
            } finally {
                setLoading(false);
            }
        };

        fetchPrescriptions();
    }, []);

    const filtered = prescriptions.filter(p => {
        if (!searchTerm.trim()) return true;
        const q = searchTerm.toLowerCase();
        return (
            p.code.toLowerCase().includes(q) ||
            p.doctorName.toLowerCase().includes(q) ||
            p.specialtyName.toLowerCase().includes(q) ||
            p.diagnosis.toLowerCase().includes(q) ||
            p.items.some(i => i.name.toLowerCase().includes(q))
        );
    });

    const handlePrint = (_prescriptionId?: number) => {
        // Trigger browser print
        window.print();
    };

    const getStatusBadge = (status: string) => {
        if (status === 'Dispensed') {
            return (
                <span style={{ backgroundColor: '#DEF7EC', color: '#03543F', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                    <CheckCircle size={14} /> Đã cấp thuốc
                </span>
            );
        }
        return (
            <span style={{ backgroundColor: '#FEF08A', color: '#854D0E', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                <Clock4 size={14} /> Chờ quầy dược cấp
            </span>
        );
    };

    return (
        <div style={{ maxWidth: 1000, margin: '0 auto' }}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Đơn thuốc của tôi' }
            ]} />
            
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '16px' }}>
                <div>
                    <h2 style={{ color: 'var(--c-navy)', margin: 0 }}>Đơn thuốc điện tử</h2>
                    <p style={{ margin: '4px 0 0', color: 'var(--c-text-light)', fontSize: '0.9rem' }}>
                        Xem lịch sử đơn thuốc được bác sĩ chỉ định và theo dõi tình trạng cấp phát
                    </p>
                </div>
                <div style={{ position: 'relative', width: '300px' }}>
                    <Search size={18} style={{ position: 'absolute', left: '12px', top: '10px', color: 'var(--c-muted)' }} />
                    <input 
                        type="text" 
                        className="form-input" 
                        placeholder="Tìm theo mã đơn, bác sĩ, tên thuốc..." 
                        value={searchTerm}
                        onChange={(e) => setSearchTerm(e.target.value)}
                        style={{ paddingLeft: '40px' }} 
                    />
                </div>
            </div>

            {errorMsg && (
                <div style={{ padding: '12px 16px', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#991b1b', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <AlertCircle size={18} /> {errorMsg}
                </div>
            )}

            {loading ? (
                <div style={{ textAlign: 'center', padding: '40px', color: 'var(--c-text-light)' }}>
                    Đang tải danh sách đơn thuốc từ hệ thống...
                </div>
            ) : filtered.length === 0 ? (
                <div className="card" style={{ textAlign: 'center', padding: '48px 20px', color: 'var(--c-text-light)' }}>
                    <Pill size={48} style={{ margin: '0 auto 16px', color: '#cbd5e1' }} />
                    <h3 style={{ margin: '0 0 8px', color: 'var(--c-navy)' }}>Chưa có đơn thuốc nào</h3>
                    <p style={{ margin: 0, fontSize: '0.95rem' }}>
                        {searchTerm ? 'Không tìm thấy đơn thuốc khớp với từ khóa tìm kiếm.' : 'Các đơn thuốc do bác sĩ kê sau khi hoàn tất ca khám sẽ hiển thị tại đây.'}
                    </p>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                    {filtered.map(p => (
                        <div key={p.id} className="card" style={{ padding: '0', overflow: 'hidden', border: '1px solid #e2e8f0', borderRadius: '14px' }}>
                            {/* Header */}
                            <div style={{ padding: '16px 24px', backgroundColor: '#f8fafc', borderBottom: '1px solid var(--c-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '12px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
                                    <div style={{ width: '42px', height: '42px', borderRadius: '10px', backgroundColor: '#e0f2fe', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                        <FileText color="#0284c7" size={22} />
                                    </div>
                                    <div>
                                        <div style={{ fontWeight: 700, color: 'var(--c-navy)', fontSize: '1.05rem', display: 'flex', alignItems: 'center', gap: '10px' }}>
                                            <span>Mã đơn: {p.code}</span>
                                            {getStatusBadge(p.status)}
                                        </div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-text-light)', display: 'flex', alignItems: 'center', gap: '12px', marginTop: '4px' }}>
                                            <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                <CalendarDays size={14} /> Ngày kê: {p.appointmentDate}
                                            </span>
                                            {p.appointmentCode && (
                                                <span>Lịch hẹn: <strong>{p.appointmentCode}</strong></span>
                                            )}
                                        </div>
                                    </div>
                                </div>
                                <button 
                                    onClick={() => handlePrint(p.id)} 
                                    className="btn-outline" 
                                    style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '6px 14px', fontSize: '0.875rem' }}
                                    title="In đơn thuốc"
                                >
                                    <Printer size={16} /> In đơn thuốc
                                </button>
                            </div>
                            
                            {/* Body */}
                            <div style={{ padding: '24px' }}>
                                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '16px', marginBottom: '20px', backgroundColor: '#f8fafc', padding: '14px 18px', borderRadius: '10px' }}>
                                    <div>
                                        <p style={{ margin: '0 0 4px', fontSize: '0.85rem', color: 'var(--c-text-light)' }}>Bác sĩ chỉ định</p>
                                        <p style={{ margin: 0, fontWeight: 600, color: '#0f172a' }}>{p.doctorName} ({p.specialtyName})</p>
                                    </div>
                                    <div>
                                        <p style={{ margin: '0 0 4px', fontSize: '0.85rem', color: 'var(--c-text-light)' }}>Chẩn đoán / Tóm tắt ca khám</p>
                                        <p style={{ margin: 0, fontWeight: 600, color: '#0f172a' }}>{p.diagnosis}</p>
                                    </div>
                                </div>

                                <div>
                                    <h4 style={{ margin: '0 0 12px 0', color: 'var(--c-navy)', display: 'flex', alignItems: 'center', gap: '8px', fontSize: '1rem' }}>
                                        <Pill size={18} color="#0284c7" /> Danh sách thuốc chỉ định ({p.items.length})
                                    </h4>
                                    <div className="table-responsive">
                                        <table className="table" style={{ margin: 0, fontSize: '0.9rem' }}>
                                            <thead>
                                                <tr style={{ backgroundColor: '#f1f5f9' }}>
                                                    <th style={{ width: '40px' }}>STT</th>
                                                    <th>Tên thuốc</th>
                                                    <th>Liều dùng</th>
                                                    <th>Tần suất</th>
                                                    <th>Số ngày</th>
                                                    <th style={{ textAlign: 'right' }}>Số lượng</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                {p.items.map((m, i) => (
                                                    <tr key={i}>
                                                        <td style={{ color: '#64748b' }}>{i + 1}</td>
                                                        <td style={{ fontWeight: 600, color: '#0f172a' }}>
                                                            {m.name}
                                                            {m.instructions && (
                                                                <div style={{ fontSize: '0.8rem', color: '#64748b', fontWeight: 400, marginTop: '2px' }}>
                                                                    💡 {m.instructions}
                                                                </div>
                                                            )}
                                                        </td>
                                                        <td>{m.dosage}</td>
                                                        <td>{m.frequency}</td>
                                                        <td>{m.durationDays ? `${m.durationDays} ngày` : '-'}</td>
                                                        <td style={{ textAlign: 'right', fontWeight: 700, color: '#0284c7' }}>
                                                            {m.quantity} {m.unit}
                                                        </td>
                                                    </tr>
                                                ))}
                                            </tbody>
                                        </table>
                                    </div>
                                </div>

                                {p.notes && (
                                    <div style={{ marginTop: '20px', padding: '14px 18px', backgroundColor: '#fffbeb', borderRadius: '8px', borderLeft: '4px solid #f59e0b' }}>
                                        <p style={{ margin: '0 0 4px', fontWeight: 700, color: '#92400e', fontSize: '0.8rem', textTransform: 'uppercase' }}>Lời dặn của bác sĩ điều trị</p>
                                        <p style={{ margin: 0, color: '#78350f', fontSize: '0.9rem' }}>{p.notes}</p>
                                    </div>
                                )}
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
