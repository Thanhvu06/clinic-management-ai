import React from 'react';
import { Link } from 'react-router-dom';
import { ShieldPlus } from 'lucide-react';
import styles from './forms.module.css';

export interface AuthShellProps {
    title: string;
    subtitle?: string;
    children: React.ReactNode;
    footerLink?: React.ReactNode;
}

export const AuthShell: React.FC<AuthShellProps> = ({ title, subtitle, children, footerLink }) => (
    <div className={styles.authContainer}>
        <div className={styles.authBox}>
            <Link to="/" className={styles.authLogo}>
                <ShieldPlus size={22} aria-hidden="true" />
                ClinicCare
            </Link>
            <div className={styles.authCard}>
                <h1 className={styles.authTitle}>{title}</h1>
                {subtitle && <p className={styles.authSubtitle}>{subtitle}</p>}
                {children}
            </div>
            {footerLink && <div className={styles.authFooter}>{footerLink}</div>}
        </div>
    </div>
);
