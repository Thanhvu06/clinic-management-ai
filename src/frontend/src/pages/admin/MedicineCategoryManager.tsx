import { useState } from 'react';
import { medicineApi } from '../../api/medicineApi';
import type { MedicineCategoryDto, SaveMedicineCategoryDto } from '../../types';

export function MedicineCategoryManager({ categories, onChanged }: { categories: MedicineCategoryDto[]; onChanged: () => void }) {
    const [editingId, setEditingId] = useState<number | null>(null);
    const [form, setForm] = useState<SaveMedicineCategoryDto>({ name: '', description: '', sortOrder: 0 });
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    const save = async (event: React.FormEvent) => {
        event.preventDefault(); setBusy(true); setError('');
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
    return <details className="card" style={{ marginTop: 24 }}>
        <summary>Quản lý nhóm thuốc</summary>
        {error && <p role="alert">{error}</p>}
        <table className="table"><thead><tr><th>Tên nhóm</th><th>Thứ tự</th><th>Trạng thái nhóm</th><th>Thao tác nhóm</th></tr></thead>
            <tbody>{categories.map(c => <tr key={c.id}><td>{c.name}</td><td>{c.sortOrder}</td><td>{c.isActive ? 'Đang dùng' : 'Tạm ngưng'}</td><td>
                <button className="btn-secondary" disabled={busy} onClick={() => { setEditingId(c.id); setForm({ name: c.name, description: c.description, sortOrder: c.sortOrder }); }}>Sửa nhóm</button>{' '}
                <button className="btn-secondary" disabled={busy} onClick={() => toggle(c.id)}>{c.isActive ? 'Tắt nhóm' : 'Bật nhóm'}</button>
            </td></tr>)}</tbody>
        </table>
        <form onSubmit={save} style={{ display: 'flex', flexWrap: 'wrap', gap: 12, marginTop: 16 }}>
            <label>Tên nhóm thuốc<input className="form-input" required maxLength={100} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label>
            <label>Mô tả nhóm<input className="form-input" maxLength={500} value={form.description ?? ''} onChange={e => setForm({ ...form, description: e.target.value || null })} /></label>
            <label>Thứ tự nhóm<input className="form-input" type="number" value={form.sortOrder} onChange={e => setForm({ ...form, sortOrder: Number(e.target.value) })} /></label>
            <button className="btn-primary" disabled={busy}>{editingId === null ? 'Thêm nhóm thuốc' : 'Lưu nhóm thuốc'}</button>
            {editingId !== null && <button type="button" className="btn-secondary" onClick={() => { setEditingId(null); setForm({ name: '', description: '', sortOrder: 0 }); }}>Hủy sửa nhóm</button>}
        </form>
    </details>;
}
