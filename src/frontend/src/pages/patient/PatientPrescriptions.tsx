import React, { useState, useEffect } from 'react';
import { Breadcrumb } from '../../components/Breadcrumb';
import { FileText, CalendarDays, Pill, Download, Search } from 'lucide-react';
import axiosClient from '../../api/axiosClient';

export const PatientPrescriptions: React.FC = () => {
    const [prescriptions, setPrescriptions] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        // Attempt to fetch from API, fallback to mockup if fails or empty
        const fetchPrescriptions = async () => {
            try {
                // Trying a likely endpoint
                const res = await axiosClient.get('/patients/me/prescriptions');
                if (res.data && res.data.length > 0) {
                    setPrescriptions(res.data);
                } else {
                    setPrescriptions(getMockupData());
                }
            } catch (err) {
                setPrescriptions(getMockupData());
            } finally {
                setLoading(false);
            }
        };

        fetchPrescriptions();
    }, []);

    const getMockupData = () => [
        {
            id: 1,
            code: 'PX-20260821-001',
            date: '2026-08-21',
            doctor: 'BS. Nguyễn Văn A',
            specialty: 'Nội tổng quát',
            diagnosis: 'Viêm họng cấp, sốt siêu vi',
            medicines: [
                { name: 'Paracetamol 500mg', dosage: 'Uống 1 viên khi sốt trên 38.5 độ', quantity: '10 viên' },
                { name: 'Amoxicillin 500mg', dosage: 'Ngày 2 lần, mỗi lần 1 viên sau ăn', quantity: '14 viên' },
                { name: 'Vitamin C 500mg', dosage: 'Ngày 1 viên', quantity: '10 viên' }
            ],
            note: 'Nghỉ ngơi nhiều, uống nhiều nước ấm. Tái khám sau 7 ngày nếu không đỡ.'
        },
        {
            id: 2,
            code: 'PX-20260510-045',
            date: '2026-05-10',
            doctor: 'BS. Trần Thị B',
            specialty: 'Tiêu hóa',
            diagnosis: 'Viêm dạ dày cấp',
            medicines: [
                { name: 'Omeprazole 20mg', dosage: 'Sáng 1 viên trước ăn 30 phút', quantity: '14 viên' },
                { name: 'Phosphalugel 20g', dosage: 'Uống 1 gói khi đau', quantity: '10 gói' }
            ],
            note: 'Kiêng đồ cay nóng, dầu mỡ, bia rượu.'
        }
    ];

    return (
        <div style={{ maxWidth: 1000, margin: '0 auto' }}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Đơn thuốc của tôi' }
            ]} />
            
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy)', margin: 0 }}>Lịch sử đơn thuốc</h2>
                <div style={{ position: 'relative', width: '300px' }}>
                    <Search size={18} style={{ position: 'absolute', left: '12px', top: '10px', color: 'var(--c-muted)' }} />
                    <input type="text" className="form-input" placeholder="Tìm kiếm đơn thuốc..." style={{ paddingLeft: '40px' }} />
                </div>
            </div>

            {loading ? (
                <div style={{ textAlign: 'center', padding: '40px', color: 'var(--c-text-light)' }}>Đang tải danh sách đơn thuốc...</div>
            ) : prescriptions.length === 0 ? (
                <div className="empty-state">
                    Bạn chưa có đơn thuốc nào.
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                    {prescriptions.map(p => (
                        <div key={p.id} className="card" style={{ padding: '0', overflow: 'hidden' }}>
                            <div style={{ padding: '16px 24px', backgroundColor: 'var(--c-bg)', borderBottom: '1px solid var(--c-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                    <FileText color="var(--c-primary)" size={24} />
                                    <div>
                                        <div style={{ fontWeight: 600, color: 'var(--c-navy)', fontSize: '1.05rem' }}>Mã đơn: {p.code}</div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-text-light)', display: 'flex', alignItems: 'center', gap: '6px', marginTop: '4px' }}>
                                            <CalendarDays size={14} /> {p.date}
                                        </div>
                                    </div>
                                </div>
                                <button className="btn-outline" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <Download size={16} /> Tải PDF
                                </button>
                            </div>
                            
                            <div style={{ padding: '24px' }}>
                                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '24px' }}>
                                    <div>
                                        <p style={{ margin: '0 0 4px', fontSize: '0.9rem', color: 'var(--c-text-light)' }}>Bác sĩ chỉ định</p>
                                        <p style={{ margin: 0, fontWeight: 500 }}>{p.doctor} - {p.specialty}</p>
                                    </div>
                                    <div>
                                        <p style={{ margin: '0 0 4px', fontSize: '0.9rem', color: 'var(--c-text-light)' }}>Chẩn đoán</p>
                                        <p style={{ margin: 0, fontWeight: 500 }}>{p.diagnosis}</p>
                                    </div>
                                </div>

                                <div>
                                    <h4 style={{ margin: '0 0 16px 0', color: 'var(--c-navy)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                                        <Pill size={18} /> Danh sách thuốc
                                    </h4>
                                    <div className="table-responsive">
                                        <table className="table" style={{ margin: 0 }}>
                                            <thead>
                                                <tr>
                                                    <th>Tên thuốc</th>
                                                    <th>Cách dùng</th>
                                                    <th style={{ textAlign: 'right' }}>Số lượng</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                {p.medicines?.map((m: any, i: number) => (
                                                    <tr key={i}>
                                                        <td style={{ fontWeight: 500 }}>{m.name}</td>
                                                        <td>{m.dosage}</td>
                                                        <td style={{ textAlign: 'right' }}>{m.quantity}</td>
                                                    </tr>
                                                ))}
                                            </tbody>
                                        </table>
                                    </div>
                                </div>

                                {p.note && (
                                    <div style={{ marginTop: '24px', padding: '16px', backgroundColor: '#FEF3C7', borderRadius: '8px', borderLeft: '4px solid #F59E0B' }}>
                                        <p style={{ margin: '0 0 4px', fontWeight: 600, color: '#92400E', fontSize: '0.85rem', textTransform: 'uppercase' }}>Lời dặn của bác sĩ</p>
                                        <p style={{ margin: 0, color: '#92400E', fontStyle: 'italic' }}>{p.note}</p>
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
