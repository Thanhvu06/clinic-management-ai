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
                    <div className={styles.spacing1}>
                        <div className={styles.flexLayout1}>
                            <ShieldPlus size={32} />
                        </div>
                        <h1 className={styles.heading1}>{title}</h1>
                        <p className={styles.paragraph1}>{subtitle}</p>
                    </div>
                    
                    {children}

                    {footerLink && (
                        <div className={styles.text1}>
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
                            className={styles.image1}
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
