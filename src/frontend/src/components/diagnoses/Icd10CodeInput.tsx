import { useEffect, useState } from 'react';
import { AutoComplete, Button } from 'antd';
import { icd10Api } from '../../api/icd10Api';
import type { Icd10Code } from '../../types/icd10';
import styles from './Icd10CodeInput.module.css';

interface Props {
    value: string;
    onChange: (value: string) => void;
    onSelect: (code: Icd10Code) => void;
    className?: string;
}

export function Icd10CodeInput({ value, onChange, onSelect, className }: Props) {
    const [search, setSearch] = useState('');
    const [codes, setCodes] = useState<Icd10Code[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(false);
    const [retry, setRetry] = useState(0);

    useEffect(() => {
        if (!search.trim()) return;
        let cancelled = false;
        const timer = setTimeout(async () => {
            setLoading(true);
            try {
                const response = await icd10Api.search(search.trim(), 20);
                if (!response.success) throw new Error('ICD-10 search failed');
                if (!cancelled) {
                    setCodes(response.data ?? []);
                    setError(false);
                }
            } catch {
                if (!cancelled) {
                    setCodes([]);
                    setError(true);
                }
            } finally {
                if (!cancelled) setLoading(false);
            }
        }, 300);
        return () => { cancelled = true; clearTimeout(timer); };
    }, [search, retry]);

    return <div className={styles.field}>
        <AutoComplete
            id="diagnosis-code"
            aria-label="Mã ICD-10"
            value={value}
            className={className}
            placeholder="J02.9"
            options={codes.map(code => ({ value: code.code, label: `${code.code} – ${code.nameVi}` }))}
            onChange={onChange}
            onSearch={query => {
                setSearch(query);
                setCodes([]);
                setError(false);
                setLoading(Boolean(query.trim()));
            }}
            onSelect={code => {
                const selected = codes.find(item => item.code === code);
                if (selected) onSelect(selected);
            }}
            notFoundContent={loading ? <span role="status">Đang tải danh mục ICD-10...</span>
                : error ? null : 'Không tìm thấy mã phù hợp'}
        />
        {error && <div className={styles.error} role="alert">
            <span>Không tải được danh mục ICD-10</span>
            <Button size="small" disabled={loading} onClick={() => { setLoading(true); setRetry(current => current + 1); }}>
                {loading ? 'Đang thử lại...' : 'Thử lại'}
            </Button>
        </div>}
    </div>;
}
