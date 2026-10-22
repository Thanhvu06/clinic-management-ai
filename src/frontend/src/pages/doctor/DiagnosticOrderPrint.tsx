import React, { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Button, Flex } from 'antd';
import { Printer, ArrowLeft, ShieldPlus } from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto } from '../../types';
import { LoadingState, InlineError } from '../../components/common';
import styles from './DiagnosticOrderPrint.module.css';

export const DiagnosticOrderPrint: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const orderId = Number(id);
    const navigate = useNavigate();

    const [order, setOrder] = useState<DiagnosticOrderDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!orderId) return;
        let isMounted = true;
        diagnosticApi.getDoctorOrderById(orderId)
            .then(res => {
                if (!isMounted) return;
                if (res.success && res.data) {
                    setOrder(res.data);
                } else {
                    setError('Không tìm thấy thông tin phiếu chỉ định.');
                }
            })
            .catch(err => {
                if (!isMounted) return;
                setError(err.message || 'Lỗi khi tải phiếu chỉ định.');
            })
            .finally(() => {
                if (isMounted) setLoading(false);
            });
        return () => { isMounted = false; };
    }, [orderId]);

    const handlePrint = () => {
        window.print();
    };

    if (loading) {
        return <LoadingState message="Đang tải thông tin phiếu chỉ định..." height="300px" />;
    }

    if (error || !order) {
        return (
            <div className={styles.spacing1}>
                <InlineError message={error || 'Không tìm thấy phiếu chỉ định.'} />
                <Flex justify="center" className={styles.spacing2}>
                    <Button type="primary" onClick={() => navigate(-1)}>Quay lại</Button>
                </Flex>
            </div>
        );
    }

    const categoryMap: Record<string, string> = {
        Laboratory: 'Xét nghiệm',
        Ultrasound: 'Siêu âm',
        Imaging: 'Chẩn đoán hình ảnh',
        Other: 'Khác'
    };

    const statusMap: Record<string, string> = {
        Ordered: 'Chờ thực hiện',
        InProgress: 'Đang thực hiện',
        Completed: 'Đã có kết quả',
        Cancelled: 'Đã hủy'
    };

    const formattedDate = new Date(order.orderedAtUtc).toLocaleString('vi-VN', {
        hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit', year: 'numeric'
    });

    const formatGender = (gender?: string | null) => {
        if (!gender) return 'Chưa cập nhật';
        const g = gender.trim().toLowerCase();
        if (g === 'male' || g === 'nam') return 'Nam';
        if (g === 'female' || g === 'nữ' || g === 'nu') return 'Nữ';
        return 'Chưa cập nhật';
    };

    return (
        <div className={styles.surface1}>
            <style>{`
                @media print {
                    body { background: var(--cc-color-bg-container) !important; margin: 0 !important; }
                    .no-print { display: none !important; }
                    .print-container { 
                        box-shadow: none !important; 
                        border: none !important; 
                        margin: 0 !important; 
                        padding: 0 !important; 
                        width: 100% !important; 
                        max-width: 100% !important; 
                    }
                }
            `}</style>

            {/* Action Bar (Screen Only) */}
            <Flex justify="space-between" align="center" className={["no-print", styles.spacing3].join(' ')}>
                <Button icon={<ArrowLeft size={16} />} onClick={() => navigate(-1)}>
                    Quay lại
                </Button>
                <Button type="primary" icon={<Printer size={16} />} onClick={handlePrint}>
                    In phiếu chỉ định
                </Button>
            </Flex>

            {/* Print Slip Document */}
            <div className={["print-container", styles.surface2].join(' ')}>
                {/* Header */}
                <div className={styles.flexLayout1}>
                    <div className={styles.flexLayout2}>
                        <div className={styles.flexLayout3}>
                            <ShieldPlus size={32} />
                        </div>
                        <div>
                            <div className={styles.text1}>ClinicCare AI – Hệ thống phòng khám đa khoa</div>
                            <div className={styles.text2}>Dữ liệu phục vụ trình diễn học tập, không phải chứng từ y tế chính thức.</div>
                        </div>
                    </div>
                    <div className={styles.spacing4}>
                        <div className={styles.text3}>MÃ PHIẾU: {order.orderCode}</div>
                        <div className={styles.text2}>Mã lịch hẹn: #{order.appointmentCode}</div>
                        <div className={styles.text2}>Ngày lập: {formattedDate}</div>
                    </div>
                </div>

                {/* Slip Title */}
                <div className={styles.spacing5}>
                    <h1 className={styles.heading1}>
                        Phiếu Chỉ Định Cận Lâm Sàng
                    </h1>
                    <div className={styles.text4}>
                        (Vui lòng mang phiếu này đến phòng xét nghiệm / chẩn đoán hình ảnh)
                    </div>
                </div>

                {/* Patient Information Box */}
                <div className={styles.text5}>
                    <div className={styles.gridLayout1}>
                        <div>
                            <strong>Họ và tên:</strong> <span className={styles.text6}>{order.patientName}</span>
                        </div>
                        <div>
                            <strong>Giới tính:</strong> {formatGender(order.patientGender)}
                        </div>
                        <div>
                            <strong>Tuổi:</strong> {order.patientAge ? `${order.patientAge} tuổi` : '---'}
                        </div>
                        <div>
                            <strong>Số điện thoại:</strong> {order.patientPhone || '---'}
                        </div>
                        <div>
                            <strong>Bác sĩ chỉ định:</strong> {order.orderingDoctorName}
                        </div>
                        <div>
                            <strong>Chuyên khoa:</strong> {order.specialtyName || '---'}
                        </div>
                    </div>
                    <div className={styles.spacing6}>
                        <div><strong>Chỉ định lâm sàng / Lý do:</strong> {order.clinicalIndication}</div>
                        {order.note && <div className={styles.spacing7}><strong>Ghi chú bổ sung:</strong> {order.note}</div>}
                    </div>
                </div>

                {/* Ordered Items Table */}
                <div className={styles.spacing8}>
                    <div className={styles.text7}>
                        Danh Mục Dịch Vụ Chỉ Định ({order.items.length})
                    </div>
                    <div className={styles.tableScroll}>
                        <table className={styles.tableLayout1}>
                            <thead>
                                <tr className={styles.tableRow1}>
                                    <th className={styles.tableHeading1}>STT</th>
                                    <th className={styles.tableHeading2}>Mã dịch vụ</th>
                                    <th className={styles.tableHeading3}>Tên dịch vụ cận lâm sàng</th>
                                    <th className={styles.tableHeading4}>Phân loại</th>
                                    <th className={styles.tableHeading5}>Trạng thái</th>
                                </tr>
                            </thead>
                            <tbody>
                                {order.items.map((item, idx) => (
                                    <tr key={item.id} className={styles.tableRow2}>
                                        <td className={styles.tableCell1}>{idx + 1}</td>
                                        <td className={styles.tableCell2}>{item.serviceCode}</td>
                                        <td className={styles.tableCell3}>
                                            <div>{item.serviceName}</div>
                                            {item.preparationInstructions && (
                                                <div className={styles.text8}>
                                                    Chuẩn bị: {item.preparationInstructions}
                                                </div>
                                            )}
                                        </td>
                                        <td className={styles.tableCell4}>{categoryMap[item.category] || item.category}</td>
                                        <td className={styles.tableCell5}>
                                            <span className={[styles.text9, (item.status === 'Completed' ? styles.state1 : (item.status === 'InProgress' ? styles.state2 : styles.state3)), (item.status === 'Completed' ? styles.state4 : (item.status === 'InProgress' ? styles.state5 : styles.state6))].join(' ')}>
                                                {statusMap[item.status] || item.status}
                                            </span>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </div>

                {/* Signatures */}
                <div className={styles.gridLayout2}>
                    <div>
                        <div className={styles.text10}>KỸ THUẬT VIÊN THỰC HIỆN</div>
                        <div className={styles.text11}>(Ký và ghi rõ họ tên)</div>
                        <div className={styles.text12}>
                            {order.completedByUserName || '................................................'}
                        </div>
                    </div>
                    <div>
                        <div className={styles.text10}>BÁC SĨ CHỈ ĐỊNH</div>
                        <div className={styles.text11}>(Ký và ghi rõ họ tên)</div>
                        <div className={styles.text13}>
                            {order.orderingDoctorName}
                        </div>
                    </div>
                </div>

                {/* Footer Note */}
                <div className={styles.text14}>
                </div>
            </div>
        </div>
    );
};
