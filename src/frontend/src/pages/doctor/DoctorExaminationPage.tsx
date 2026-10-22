import React from 'react';
import { Link } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { DoctorExaminationWorkspace } from './DoctorExaminationWorkspace';
import { DoctorExaminationFrame } from './DoctorExaminationHeader';
import styles from './DoctorExaminationPage.module.css';

export const DoctorExaminationPage: React.FC = () => (
    <>
        <Link to="/doctor" className={styles.backLink}>
            <ArrowLeft size={14} aria-hidden="true" />
            <span>Quay lại bàn làm việc bác sĩ</span>
        </Link>
        <DoctorExaminationFrame><DoctorExaminationWorkspace /></DoctorExaminationFrame>
    </>
);
