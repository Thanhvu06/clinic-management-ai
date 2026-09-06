import React from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';

interface PaginationProps {
    page: number;
    totalPages: number;
    totalRecords?: number;
    onPageChange: (newPage: number) => void;
}

export const Pagination: React.FC<PaginationProps> = ({
    page,
    totalPages,
    totalRecords,
    onPageChange
}) => {
    if (totalPages <= 1) return null;

    return (
        <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: '12px',
            marginTop: '20px',
            padding: '12px 16px',
            backgroundColor: 'white',
            borderRadius: 'var(--radius-md)',
            border: '1px solid var(--c-border)'
        }}>
            <div style={{ fontSize: '0.85rem', color: 'var(--c-text-light)' }}>
                Trang <strong style={{ color: 'var(--c-text-dark)' }}>{page}</strong> / {totalPages}
                {totalRecords !== undefined && (
                    <span> (Tổng số: <strong style={{ color: 'var(--c-text-dark)' }}>{totalRecords}</strong> bản ghi)</span>
                )}
            </div>

            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <button
                    type="button"
                    className="btn-secondary"
                    disabled={page <= 1}
                    onClick={() => onPageChange(page - 1)}
                    style={{ padding: '6px 12px', height: '32px', fontSize: '0.82rem' }}
                >
                    <ChevronLeft size={16} />
                    <span>Trước</span>
                </button>

                <button
                    type="button"
                    className="btn-secondary"
                    disabled={page >= totalPages}
                    onClick={() => onPageChange(page + 1)}
                    style={{ padding: '6px 12px', height: '32px', fontSize: '0.82rem' }}
                >
                    <span>Sau</span>
                    <ChevronRight size={16} />
                </button>
            </div>
        </div>
    );
};
