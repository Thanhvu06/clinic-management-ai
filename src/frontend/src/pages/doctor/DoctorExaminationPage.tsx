import React from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { PageHeader } from '../../components/common';
import { DoctorExaminationWorkspace } from './DoctorExaminationWorkspace';
import styles from './DoctorExaminationPage.module.css';

// Tiêu đề đặt ở đây vì DoctorExaminationWorkspace.tsx đã quá 1.000 dòng.
export const DoctorExaminationPage: React.FC = () => (
    <>
        <Link to="/doctor" className={styles.backLink}>
            <ArrowLeft size={14} aria-hidden="true" />
            <span>Quay lại bàn làm việc bác sĩ</span>
        </Link>
        <PageHeader title="Bàn khám lâm sàng" />
        <DoctorExaminationWorkspace />
    </>
);
