import React from 'react';
import { Search, RotateCcw } from 'lucide-react';

interface FilterBarProps {
    searchTerm?: string;
    onSearchChange?: (val: string) => void;
    searchPlaceholder?: string;
    children?: React.ReactNode;
    onReset?: () => void;
}

export const FilterBar: React.FC<FilterBarProps> = ({
    searchTerm,
    onSearchChange,
    searchPlaceholder = 'Tìm kiếm...',
    children,
    onReset
}) => {
    return (
        <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: '12px',
            backgroundColor: 'white',
            padding: '16px',
            borderRadius: 'var(--radius-lg)',
            border: '1px solid var(--c-border)',
            marginBottom: '20px',
            boxShadow: 'var(--shadow-sm)'
        }}>
            <div style={{
                display: 'flex',
                alignItems: 'center',
                flexWrap: 'wrap',
                gap: '12px',
                flex: 1,
                minWidth: '280px'
            }}>
                {onSearchChange !== undefined && (
                    <div style={{ position: 'relative', flex: '1 1 240px', maxWidth: '360px' }}>
                        <Search 
                            size={16} 
                            style={{
                                position: 'absolute',
                                left: '12px',
                                top: '50%',
                                transform: 'translateY(-50%)',
                                color: 'var(--c-muted)',
                                pointerEvents: 'none'
                            }} 
                        />
                        <input
                            type="text"
                            className="form-input"
                            style={{ paddingLeft: '36px' }}
                            placeholder={searchPlaceholder}
                            value={searchTerm || ''}
                            onChange={(e) => onSearchChange(e.target.value)}
                        />
                    </div>
                )}
                {children}
            </div>

            {onReset && (
                <button 
                    type="button" 
                    className="btn-secondary"
                    onClick={onReset}
                    style={{ fontSize: '0.85rem', padding: '8px 14px' }}
                    title="Đặt lại bộ lọc"
                >
                    <RotateCcw size={14} />
                    <span>Làm mới</span>
                </button>
            )}
        </div>
    );
};
