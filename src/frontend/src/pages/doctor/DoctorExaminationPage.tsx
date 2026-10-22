import React from 'react';
import { PageHeader } from '../../components/common';
import { DoctorExaminationWorkspace } from './DoctorExaminationWorkspace';

// Tiêu đề đặt ở đây vì DoctorExaminationWorkspace.tsx đã quá 1.000 dòng.
export const DoctorExaminationPage: React.FC = () => (
    <>
        <PageHeader title="Bàn khám lâm sàng" />
        <DoctorExaminationWorkspace />
    </>
);
