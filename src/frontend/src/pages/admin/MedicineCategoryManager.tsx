import { useState } from 'react';
import { Alert, Button, Collapse, Form, Input, InputNumber, Tag } from 'antd';
import { DataTable, EmptyState } from '../../components/common';
import { medicineApi } from '../../api/medicineApi';
import type { MedicineCategoryDto, SaveMedicineCategoryDto } from '../../types';
import styles from './MedicineCategoryManager.module.css';

export function MedicineCategoryManager({ categories, onChanged }: { categories: MedicineCategoryDto[]; onChanged: () => void }) {
    const [editingId, setEditingId] = useState<number | null>(null);
    const [form, setForm] = useState<SaveMedicineCategoryDto>({ name: '', description: '', sortOrder: 0 });
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    const save = async () => {
        setBusy(true); setError('');
        try {
            if (editingId !== null) await medicineApi.updateCategory(editingId, form);
            else await medicineApi.createCategory(form);
            setEditingId(null); setForm({ name: '', description: '', sortOrder: 0 }); onChanged();
        } catch (e) { setError((e as { message?: string }).message || 'Không thể lưu nhóm thuốc.'); }
        finally { setBusy(false); }
    };
    const toggle = async (id: number) => {
        setBusy(true); setError('');
        try { await medicineApi.toggleCategory(id); onChanged(); }
        catch (e) { setError((e as { message?: string }).message || 'Không thể đổi trạng thái nhóm thuốc.'); }
        finally { setBusy(false); }
    };
    return <Collapse className={styles.categoryManager} items={[{
        key: 'categories', label: 'Quản lý nhóm thuốc', children: <>
            {error && <Alert className={styles.categoryError} type="error" title={error} showIcon />}
            {categories.length === 0 ? <EmptyState title="Không có dữ liệu hiển thị." /> : <DataTable data={categories} keyExtractor={category => category.id} columns={[
                { header: 'Tên nhóm', accessor: 'name' },
                { header: 'Thứ tự', accessor: 'sortOrder' },
                { header: 'Trạng thái nhóm', accessor: category => <Tag color={category.isActive ? 'success' : 'default'}>{category.isActive ? 'Đang dùng' : 'Tạm ngưng'}</Tag> },
                { header: 'Thao tác nhóm', accessor: category => <div className={styles.categoryRowActions}>
                    <Button disabled={busy} onClick={() => { setEditingId(category.id); setForm({ name: category.name, description: category.description, sortOrder: category.sortOrder }); }}>Sửa nhóm</Button>
                    <Button disabled={busy} onClick={() => toggle(category.id)}>{category.isActive ? 'Tắt nhóm' : 'Bật nhóm'}</Button>
                </div> }
            ]} />}
            <Form className={styles.categoryForm} layout="inline" onFinish={save} disabled={busy}>
                <Form.Item label="Tên nhóm thuốc" htmlFor="categoryName"><Input id="categoryName" required maxLength={100} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></Form.Item>
                <Form.Item label="Mô tả nhóm" htmlFor="categoryDescription"><Input id="categoryDescription" maxLength={500} value={form.description ?? ''} onChange={e => setForm({ ...form, description: e.target.value || null })} /></Form.Item>
                <Form.Item label="Thứ tự nhóm" htmlFor="categoryOrder"><InputNumber id="categoryOrder" value={form.sortOrder} onChange={value => setForm({ ...form, sortOrder: value ?? 0 })} /></Form.Item>
                <Button type="primary" htmlType="submit" loading={busy} disabled={busy}>{editingId === null ? 'Thêm nhóm thuốc' : 'Lưu nhóm thuốc'}</Button>
                {editingId !== null && <Button disabled={busy} onClick={() => { setEditingId(null); setForm({ name: '', description: '', sortOrder: 0 }); }}>Hủy sửa nhóm</Button>}
            </Form>
        </>
    }]} />;
}
