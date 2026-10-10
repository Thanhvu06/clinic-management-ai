import React, { useCallback, useEffect, useState } from 'react';
import { Button, Card, Col, DatePicker, Form, Input, Row, Select } from 'antd';
import dayjs from 'dayjs';
import { Mail, MapPin, Phone, Save, User } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { Breadcrumb } from '../../components/Breadcrumb';
import { PageHeader } from '../../components/common/PageHeader';
import { EmptyState } from '../../components/common/EmptyState';
import { InlineError } from '../../components/common/InlineError';
import { LoadingState } from '../../components/common/LoadingState';
import styles from './PatientProfile.module.css';

interface Profile {
    id: number;
    fullName: string;
    email: string;
    phoneNumber: string;
    dateOfBirth?: string | null;
    gender?: string | number | null;
    address?: string;
}

export const PatientProfile: React.FC = () => {
    const [profile, setProfile] = useState<Profile | null>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const { showAlert } = useDialog();
    const fetchProfile = useCallback(async () => {
        setLoading(true);
        setError('');
        try {
            const res = await axiosClient.get<any, ApiResponse<Profile>>('/patients/me');
            if (!res.success) throw new Error(res.message || 'Không thể tải thông tin hồ sơ.');
            setProfile(res.data ? { ...res.data, dateOfBirth: res.data.dateOfBirth?.split('T')[0] || null } : null);
        } catch (err: any) {
            setError(err?.message || 'Không thể tải thông tin hồ sơ.');
        } finally {
            setLoading(false);
        }
    }, []);
    useEffect(() => { void fetchProfile(); }, [fetchProfile]);
    const update = (changes: Partial<Profile>) => setProfile(current => current ? { ...current, ...changes } : current);
    const handleSubmit = async () => {
        if (!profile || saving) return;
        setSaving(true);
        try {
            const res = await axiosClient.put<any, ApiResponse<Profile>>('/patients/me', {
                fullName: profile.fullName, phoneNumber: profile.phoneNumber,
                dateOfBirth: profile.dateOfBirth || null,
                gender: profile.gender === '' || profile.gender == null ? null : Number(profile.gender), address: profile.address
            });
            if (!res.success) throw new Error(res.message || 'Có lỗi xảy ra khi cập nhật.');
            showAlert('Cập nhật hồ sơ thành công!', 'Thành công', 'success');
            await fetchProfile();
        } catch (err: any) {
            showAlert(err?.message || 'Có lỗi xảy ra khi cập nhật.', 'Lỗi', 'error');
        } finally {
            setSaving(false);
        }
    };
    return <div className={styles.page}>
        <Breadcrumb items={[{ label: 'Trang chủ', path: '/patient' }, { label: 'Hồ sơ cá nhân' }]} />
        <PageHeader title="Hồ sơ cá nhân" />
        {loading ? <LoadingState message="Đang tải thông tin..." /> : error ? <InlineError message={error} onRetry={fetchProfile} /> : !profile ? <EmptyState title="Không tìm thấy thông tin hồ sơ." /> :
            <Card><Form layout="vertical" onFinish={handleSubmit}>
                <Row gutter={[16, 0]}>
                    <Col xs={24} md={12}><Form.Item label="Họ và tên (*)" htmlFor="profile-name"><Input id="profile-name" required prefix={<User size={18} />} value={profile.fullName} onChange={e => update({ fullName: e.target.value })} /></Form.Item></Col>
                    <Col xs={24} md={12}><Form.Item label="Email" htmlFor="profile-email"><Input id="profile-email" disabled prefix={<Mail size={18} />} value={profile.email} /></Form.Item></Col>
                    <Col xs={24} md={12}><Form.Item label="Số điện thoại (*)" htmlFor="profile-phone"><Input id="profile-phone" required prefix={<Phone size={18} />} value={profile.phoneNumber} onChange={e => update({ phoneNumber: e.target.value })} /></Form.Item></Col>
                    <Col xs={24} md={12}><Form.Item label="Ngày sinh" htmlFor="profile-birth"><DatePicker id="profile-birth" className={styles.datePicker} format="DD/MM/YYYY" value={profile.dateOfBirth ? dayjs(profile.dateOfBirth) : null} onChange={date => update({ dateOfBirth: date?.format('YYYY-MM-DD') || null })} /></Form.Item></Col>
                    <Col xs={24} md={12}><Form.Item label="Giới tính" htmlFor="profile-gender"><Select id="profile-gender" aria-label="Giới tính" value={profile.gender == null || profile.gender === '' ? '' : Number(profile.gender)} options={[{ value: '', label: '-- Chưa cập nhật --' }, { value: 0, label: 'Nam' }, { value: 1, label: 'Nữ' }, { value: 2, label: 'Khác' }]} onChange={gender => update({ gender })} /></Form.Item></Col>
                    <Col xs={24} md={12}><Form.Item label="Địa chỉ" htmlFor="profile-address"><Input id="profile-address" prefix={<MapPin size={18} />} value={profile.address} onChange={e => update({ address: e.target.value })} /></Form.Item></Col>
                </Row>
                <div className={styles.actions}><Button type="primary" htmlType="submit" disabled={saving} icon={<Save size={18} />}>{saving ? 'Đang lưu...' : 'Lưu thay đổi'}</Button></div>
            </Form></Card>}
    </div>;
};
