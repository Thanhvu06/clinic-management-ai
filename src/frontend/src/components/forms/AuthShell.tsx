import React from 'react';
import { ShieldPlus, CheckCircle2, Bot, Calendar, FileText } from 'lucide-react';
import styles from './forms.module.css';

export interface AuthShellProps {
    title: string;
    subtitle: string;
    children: React.ReactNode;
    visualTitle?: string;
    visualDesc?: string;
    visualBadge?: string;
    heroTitle?: string;
    heroDescription?: string;
    heroImageUrl?: string;
    footerLink?: React.ReactNode;
}

export const AuthShell: React.FC<AuthShellProps> = ({
    title,
    subtitle,
    children,
    visualTitle,
    visualDesc,
    visualBadge = "Hệ thống Y tế Kỹ thuật số ClinicCare",
    heroTitle,
    heroDescription,
    heroImageUrl,
    footerLink
}) => {
    const displayVisualTitle = heroTitle || visualTitle || "Chăm sóc sức khỏe thông minh";
    const displayVisualDesc = heroDescription || visualDesc || "Hệ thống phòng khám đa khoa kỹ thuật số ClinicCare kết nối bạn với đội ngũ bác sĩ chuyên khoa và trợ lý y tế AI 24/7.";

    return (
        <div className={styles.authContainer}>
            <div className={styles.authBox}>
                <div className={styles.authFormCol}>
                    <div style={{ textAlign: 'center', marginBottom: '28px' }}>
                        <div style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            width: '56px',
                            height: '56px',
                            borderRadius: '16px',
                            backgroundColor: 'var(--c-primary-light)',
                            color: 'var(--c-primary)',
                            marginBottom: '14px'
                        }}>
                            <ShieldPlus size={32} />
                        </div>
                        <h1 style={{ fontSize: '1.65rem', margin: 0, color: 'var(--c-navy)', fontWeight: 700 }}>{title}</h1>
                        <p style={{ color: 'var(--c-text-light)', marginTop: '6px', fontSize: '0.925rem' }}>{subtitle}</p>
                    </div>
                    
                    {children}

                    {footerLink && (
                        <div style={{ marginTop: '24px', textAlign: 'center', fontSize: '0.925rem', color: 'var(--c-text-muted)' }}>
                            {footerLink}
                        </div>
                    )}
                </div>

                <div className={styles.authVisualCol}>
                    <div className={styles.authVisualBadge}>
                        <Bot size={16} /> {visualBadge}
                    </div>
                    {heroImageUrl && (
                        <img 
                            src={heroImageUrl} 
                            alt="Visual" 
                            style={{ width: '100%', maxHeight: '200px', objectFit: 'cover', borderRadius: '12px', marginBottom: '20px', boxShadow: 'var(--shadow-sm)' }} 
                        />
                    )}
                    <h2 className={styles.authVisualTitle}>{displayVisualTitle}</h2>
                    <p className={styles.authVisualDesc}>{displayVisualDesc}</p>
                    <div className={styles.authVisualPoints}>
                        <div className={styles.authVisualPoint}>
                            <CheckCircle2 size={18} />
                            <span>Đặt lịch khám chuyên khoa trực tuyến không chờ đợi</span>
                        </div>
                        <div className={styles.authVisualPoint}>
                            <Calendar size={18} />
                            <span>Đăng ký gói khám sức khỏe tổng quát định kỳ</span>
                        </div>
                        <div className={styles.authVisualPoint}>
                            <FileText size={18} />
                            <span>Tra cứu lịch sử đơn thuốc và kết quả chẩn đoán 24/7</span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};
