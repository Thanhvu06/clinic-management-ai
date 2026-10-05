import React from 'react';
import { Button, Card, Flex, Input } from 'antd';
import { Search, RotateCcw } from 'lucide-react';
import styles from './common.module.css';

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
        <Card size="small" className={styles.filterBar}>
            <Flex align="center" justify="space-between" wrap gap="small">
                <Flex align="center" wrap gap="small" className={styles.filterControls}>
                    {onSearchChange !== undefined && (
                        <Input
                            className={styles.filterSearch}
                            prefix={<Search size={16} className={styles.filterSearchIcon} />}
                            placeholder={searchPlaceholder}
                            value={searchTerm || ''}
                            onChange={(e) => onSearchChange(e.target.value)}
                        />
                    )}
                    {children}
                </Flex>

                {onReset && (
                    <Button
                        onClick={onReset}
                        title="Đặt lại bộ lọc"
                        icon={<RotateCcw size={14} />}
                    >
                        Làm mới
                    </Button>
                )}
            </Flex>
        </Card>
    );
};
