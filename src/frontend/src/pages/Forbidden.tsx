import React from 'react';
import { Link } from 'react-router-dom';
import { ShieldAlert } from 'lucide-react';

export const Forbidden: React.FC = () => {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', minHeight: '100vh', padding: '20px', textAlign: 'center', background: 'var(--c-bg)' }}>
            <ShieldAlert size={64} color="var(--c-danger)" style={{ marginBottom: '20px' }} />
            <h1 style={{ color: 'var(--c-navy-dark)', marginBottom: '10px' }}>Bạn không có quyền truy cập</h1>
            <p style={{ color: 'var(--c-muted)', marginBottom: '30px', maxWidth: '400px' }}>
                Tài khoản của bạn không được cấp phép để xem nội dung hoặc thực hiện thao tác này.
            </p>
            <Link to="/" className="btn-primary" style={{ textDecoration: 'none' }}>
                Quay về trang chủ
            </Link>
        </div>
    );
};
