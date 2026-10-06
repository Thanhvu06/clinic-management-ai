import React from 'react';
import { Button, Flex, Typography } from 'antd';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import styles from './common.module.css';

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
        <Flex align="center" justify="space-between" wrap gap="small" className={styles.pagination}>
            <Typography.Text type="secondary" className={styles.paginationInfo}>
                Trang <Typography.Text strong>{page}</Typography.Text> / {totalPages}
                {totalRecords !== undefined && (
                    <span> (Tổng số: <Typography.Text strong>{totalRecords}</Typography.Text> bản ghi)</span>
                )}
            </Typography.Text>

            <Flex align="center" gap="small">
                <Button
                    disabled={page <= 1}
                    onClick={() => onPageChange(page - 1)}
                    icon={<ChevronLeft size={16} />}
                >
                    Trước
                </Button>

                <Button
                    disabled={page >= totalPages}
                    onClick={() => onPageChange(page + 1)}
                    icon={<ChevronRight size={16} />}
                    iconPlacement="end"
                >
                    Sau
                </Button>
            </Flex>
        </Flex>
    );
};
